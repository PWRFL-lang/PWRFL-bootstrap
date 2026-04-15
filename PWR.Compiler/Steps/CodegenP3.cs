using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using LLVMSharp;
using LLVMSharp.Interop;

using PWR.Compiler.Ast;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.External;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Steps;

public unsafe partial class CodegenP3(LLVMContext context, LLVMModuleRef module, string filename, bool isLibrary) : VisitorCompileStep
{
	private readonly LLVMContext _context = context;
	private readonly LLVMModuleRef _module = module;
	private readonly string _filename = filename;
	private readonly bool _isLibrary = isLibrary;
	private IRBuilder _builder = null!;
	private LValueVisitor _lValueVisitor = null!;
	private readonly Stack<LLVMValueRef> _values = [];
	private readonly Stack<string> _namespaces = [];
	private LLVMValueRef _last;
	private readonly Dictionary<string, (LLVMTypeRef Type, LLVMValueRef Function)> _functions = [];
	private readonly Dictionary<string, LLVMValueRef> _locals = [];
	private readonly Dictionary<string, LLVMValueRef> _globals = [];
	private readonly Dictionary<string, LLVMTypeRef> _builtinTypes = [];
	private readonly Dictionary<string, LLVMTypeRef> _customTypes = [];
	private readonly Dictionary<LLVMTypeRef, LLVMTypeRef> _spanTypes = [];
	private readonly List<LLVMValueRef> _moduleInits = [];
	private LLVMTypeRef _mainType;
	private LLVMValueRef _currentFunc;

	public override Project Run(Project tree)
	{
		_builder = new(_context);
		_lValueVisitor = new(_module, _builder.Handle, _values, _locals, _globals, LookupType);

		LoadStdlib();
		LoadImports(tree);
		WriteMetadata(tree);

		var result = base.Run(tree);
		if (!_isLibrary) {
			BuildProgramInit(tree.Imports);
		}
		return result;
	}

	[LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
	public static partial IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string lpModuleName);

	private void LoadStdlib()
	{
		_mainType = LLVMTypeRef.CreateFunction(_context.Handle.VoidType, []);

		var callocType = LLVMTypeRef.CreateFunction(
			LLVMTypeRef.CreatePointer(_context.Handle.VoidType, 0),
			[_context.Handle.Int64Type, _context.Handle.Int64Type]
		);
		_functions.Add("calloc", (callocType, _module.AddFunction("calloc", callocType)));
		using var str2 = new MarshaledString("calloc");
		var callocAddr = NativeLibrary.GetExport(GetModuleHandle("msvcrt.dll"), "calloc");
		LLVM.AddSymbol(str2, (void*)callocAddr);

		var mallocType = LLVMTypeRef.CreateFunction(
			LLVMTypeRef.CreatePointer(_context.Handle.VoidType, 0),
			[_context.Handle.Int64Type]
		);
		_functions.Add("malloc", (mallocType, _module.AddFunction("malloc", mallocType)));
		using var str3 = new MarshaledString("malloc");
		var mallocAddr = NativeLibrary.GetExport(GetModuleHandle("msvcrt.dll"), "malloc");
		LLVM.AddSymbol(str3, (void*)mallocAddr);


		var bytesType = LLVMTypeRef.CreateArray(_context.Handle.Int8Type, 0);
		Debug.Assert(bytesType.Context.Handle == _module.Context.Handle);
		var stringType = _context.Handle.CreateNamedStruct("string");
		stringType.StructSetBody([_context.Handle.Int32Type, bytesType ], false );
		Debug.Assert(stringType.Context.Handle == _module.Context.Handle);
		var strPtr = LLVMTypeRef.CreatePointer(stringType, 0);
		Debug.Assert(strPtr.Context.Handle == _module.Context.Handle);
		_builtinTypes.Add("string", stringType);

		_builtinTypes.Add("ptr", LLVMTypeRef.CreatePointer(_context.Handle.VoidType, 0));

		/*
		var memcpyType = LLVMTypeRef.CreateFunction(
			_context.Handle.VoidType,
			[
				LLVMTypeRef.CreatePointer(_context.Handle.Int8Type, 0), // dest
				LLVMTypeRef.CreatePointer(_context.Handle.Int8Type, 0), // src
				_context.Handle.Int64Type,                              // size
				_context.Handle.Int1Type                                // isVolatile
			],
			false
		);
		var memcpy = _module.GetNamedFunction("llvm.memcpy.p0.p0.i64");
		if (memcpy.Handle == default)
			memcpy = _module.AddFunction("llvm.memcpy.p0.p0.i64", memcpyType);
		_functions.Add("memcpy", (memcpyType, memcpy));
		*/
	}

	private void LoadImports(Project tree)
	{
		var importFuncs = tree.Imports.SelectMany(i => i.Types).Cast<ExternalType>().SelectMany(t => t.Members ?? []).OfType<ExternalMethod>();
		foreach (var func in importFuncs) {
			ImportFunction(func);
		}
	}

	private void ImportFunction(ExternalMethod method)
	{
		var type = BuildFuncType(method);
		var name = method.FullName;
		var func = _module.AddFunction(name, type);
		func.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLImportStorageClass;
		_functions.Add(name, (type, func));
	}

	private void WriteMetadata(Project tree)
	{
		var header = tree.MetadataHeader;
		var metadata = tree.Metadata;
		var hdrBytes = MemoryMarshal.AsBytes(new Span<MetadataHeader>(ref header));
		byte[] bytes = [.. hdrBytes, .. metadata];
		var global = _module.AddGlobal(LLVMTypeRef.CreateArray(_context.Handle.Int8Type, (uint)bytes.Length), "$metadata$");
		var ptr = (sbyte*)NativeMemory.Alloc((nuint)bytes.Length);
		try {
			bytes.AsSpan().CopyTo(new Span<byte>(ptr, bytes.Length));
			global.Initializer = LLVM.ConstStringInContext(_context.Handle, ptr, (uint)bytes.Length, 1);
		} finally {
			NativeMemory.Free(ptr);
		}
		global.IsGlobalConstant = true;
		global.Section = ".PWRMeta";

		bytes = tree.BlobData;
		global = _module.AddGlobal(LLVMTypeRef.CreateArray(_context.Handle.Int8Type, (uint)bytes.Length), "$metaBlob$");
		ptr = (sbyte*)NativeMemory.Alloc((nuint)bytes.Length);
		try {
			bytes.AsSpan().CopyTo(new Span<byte>(ptr, bytes.Length));
			global.Initializer = LLVM.ConstStringInContext(_context.Handle, ptr, (uint)bytes.Length, 1);
		} finally {
			NativeMemory.Free(ptr);
		}
		global.IsGlobalConstant = true;
		global.Section = ".PWRBlob";
	}

	private void BuildProgramInit(List<ExternalLibrary> imports)
	{
		var funcType = LLVMTypeRef.CreateFunction(_context.Handle.Int32Type, []);
		var func = _module.AddFunction("runtimeMain", funcType);
		_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
		foreach (var im in imports) {
			var imInit = _module.AddFunction($"{im.Name}$init$", _mainType);
			imInit.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLImportStorageClass;
			_builder.Handle.BuildCall2(_mainType, imInit, []);
		}
		// _currentFunc should be Main
		_builder.Handle.BuildCall2(_mainType, _currentFunc, []);
		_builder.Handle.BuildRet(LLVM.ConstInt(_context.Handle.Int32Type, 0, 0));
	}

	public override void VisitProject(Project node)
	{
		base.VisitProject(node);
		var entryPoints = node.Files.Select(f => f.EntryPoint).Where(p => p.Handle != default).ToArray();
		node.EntryPoint = entryPoints.Length switch {
			0 => default,
			1 => entryPoints[0],
			_ => throw new Exception("Project cannot have more than one entry point")
		};
		if (_moduleInits.Count > 0) {
			var func = _module.AddFunction(_filename + "$init$", _mainType);
			_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
			foreach (var mi in _moduleInits) {
				_builder.Handle.BuildCall2(_mainType, mi, []);
			}
			_builder.Handle.BuildRetVoid();
			func.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLExportStorageClass;
		}
	}

	public override void VisitCodeFile(CodeFile node)
	{
		Visit(node.Decls);
		if (node.Body.Length > 0) {
			var main = _module.AddFunction("main", _mainType);
			_builder.Handle.PositionAtEnd(main.AppendBasicBlock("entry"));
			_currentFunc = main;
			_locals.Clear();
			Visit(node.Body);
			_builder.CreateRetVoid();
			main.VerifyFunction(LLVMVerifierFailureAction.LLVMPrintMessageAction);
			node.EntryPoint = main;
		}
	}

	public override void VisitAnnotation(Annotation node)
	{ }

	public override void VisitStructDeclaration(StructDeclaration node)
	{
		_namespaces.Push(node.Name.ToString());
		try {
			var fields = node.Body.OfType<FieldDeclaration>().ToArray();
			var inits = fields.Where(d => d.Value?.IsLiteral == false).ToArray();
			if (inits.Length > 0) {
				throw new NotImplementedException();
			}
			var typ = _context.Handle.CreateNamedStruct(node.Name.Semantic!.FullName);
			typ.StructSetBody([.. fields.Select(f => LookupType(f.Semantic!.Type))], false);
			_customTypes.Add(typ.StructName, typ);
			Visit(node.Body);
		}
		finally {  
			_namespaces.Pop();
		}
	}

	public override void VisitModuleDeclaration(ModuleDeclaration node)
	{
		_namespaces.Push(node.Name.ToString());
		try {
			base.VisitModuleDeclaration(node);
			var inits = node.Body.OfType<FieldDeclaration>().Where(d => d.Value?.IsLiteral == false).ToArray();
			if (inits.Length > 0) {
				BuildModuleInits(node, inits);
			}
		} finally {  
			_namespaces.Pop();
		}
	}

	private void BuildModuleInits(ModuleDeclaration node, FieldDeclaration[] inits)
	{
		var func = _module.AddFunction($"{node.Name}$$init", _mainType);
		_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
		foreach (var init in inits) {
			Debug.Assert(_values.Count == 0);
			Visit(init.Value);
			Debug.Assert(_values.Count == 1);
			_builder.Handle.BuildStore(_values.Pop(), _globals[string.Join('$', _namespaces.Reverse()) + '$' + init.Decl.Name]);
		}
		_builder.Handle.BuildRetVoid();
		_moduleInits.Add(func);
		func.Linkage = LLVMLinkage.LLVMInternalLinkage;
	}

	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		var funcType = BuildFuncType(node);
		if (node.Flags.HasFlag(FunctionFlags.Abstract)){
			BuildAbstractFunction(node, funcType);
			return;
		}
		var name = _namespaces.Count == 0 ? node.Name.Name : string.Join('$', _namespaces.Reverse()) + '$' + node.Name.Name;
		var func = _module.AddFunction(name, funcType);
		_functions.Add(name, (funcType, func));
		_currentFunc = func;
		_locals.Clear();

		_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
		for (int i = 0; i < node.Parameters.Length; ++i) {
			var param = func.GetParam((uint)i);
			param.Name = node.Parameters[i].Name.Name;
			var alloc = _builder.Handle.BuildAlloca(param.TypeOf, $"param${param.Name}");
			_locals[param.Name] = alloc;
			_builder.Handle.BuildStore(param, alloc);
		}

		Visit(node.Body);
		if (node.Body.Length == 0 || node.Body[^1] is not ReturnStatement) {
			if ((node.ReturnType?.Semantic?.Type ?? Types.Void) == Types.Void) {
				_builder.Handle.BuildRetVoid();
			} else {
				_builder.CreateUnreachable();
			}
		}
		func.VerifyFunction(LLVMVerifierFailureAction.LLVMPrintMessageAction);
		if (char.IsUpper(node.Name.Name[0])) {
			func.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLExportStorageClass;
		}
	}

	private void BuildAbstractFunction(FunctionDeclaration node, LLVMTypeRef funcType)
	{
		var name = _namespaces.Count == 0 ? node.Name.Name : string.Join('$', _namespaces.Reverse()) + '$' + node.Name.Name;
		var func = _module.AddFunction(node.Name.Name, funcType);

		var extModule = node.GetAncestors<ModuleDeclaration>().FirstOrDefault(m => m.HasAnnotation("ExternalLibraryName"));
		if (extModule == null) {
			throw new NotImplementedException();
		}
		func.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLImportStorageClass;
		_functions.Add(name, (funcType, func));
	}

	private LLVMTypeRef BuildFuncType(IFunction node)
		=> LLVMTypeRef.CreateFunction(
			LookupType(node.ReturnType),
			[.. node.Args.Select(p => LookupType(p.ParamType))],
			false
		);

	private LLVMTypeRef BuildFuncType(FunctionDeclaration node) => BuildFuncType((IFunction)node.Semantic!);

	private LLVMTypeRef BuildSpanType(LLVMTypeRef type, string name)
	{
		if (!_spanTypes.TryGetValue(type, out var result)) {
			result = _context.Handle.CreateNamedStruct($"span<{name}>");
			result.StructSetBody([LLVMTypeRef.CreatePointer(type, 0), _context.Handle.Int32Type], false);
			_spanTypes.Add(type, result);
		}
		return result;
	}

	private LLVMTypeRef BuildSpanType(SpanType type) => BuildSpanType(LookupType(type.BaseType), type.BaseType.Name);

	private LLVMTypeRef LookupType(IType type) => type switch {
		PrimitiveType pt => pt.Type,
		SpanType st => BuildSpanType(LookupType(st.BaseType), st.BaseType.Name),
		TypeSystem.ArrayType at => BuildSpanType(LookupType(at.BaseType), at.BaseType.Name),
		InternalPrimitiveType => LLVMTypeRef.CreatePointer(_builtinTypes[type.Name], 0),
		RefType rt => LLVMTypeRef.CreatePointer(LookupType(rt.BaseType), 0),
		InternalStruct ist => _customTypes[ist.Name],
		_ => throw new NotImplementedException()
	};

	private LLVMTypeRef LookupType(TypeReference? type)
		=> type == null ? _context.Handle.VoidType : LookupType(type.Semantic!.Type);

	private ulong SizeOf(LLVMTypeRef type)
	{
		var layout = new DataLayout(_module.DataLayout);
		return LLVM.ABISizeOfType(layout.Handle, type);
	}

	public override void VisitFieldDeclaration(FieldDeclaration node)
	{
		var sem = node.Decl.Semantic!;
		var varType = LookupType(sem.Type);
		if (sem.SemanticType.HasFlag(SemanticType.Global)) {
			if (node.VarType == VarUsage.Const) {
				VisitGlobalConstDeclaration(node.Decl, node.Value!, varType, (GlobalFieldDecl)sem);
			} else {
				var constInit = node.Value?.IsLiteral == true ? node.Value : null;
				VisitGlobalFieldDeclaration(node.Decl, constInit, varType, (GlobalFieldDecl)sem);
			}
		}
	}

	public override void VisitVarDeclarationStatement(VarDeclarationStatement node)
	{
		var sem = node.Decl.Semantic!;
		var varType = LookupType(sem.Type);
		if (sem.SemanticType.HasFlag(SemanticType.Field)) {
			if (sem.SemanticType.HasFlag(SemanticType.Global)) {
				if (node.VarType == VarUsage.Const) {
					VisitGlobalConstDeclaration(node.Decl, node.Value, varType, (GlobalFieldDecl)sem);
				} else {
					var constInit = node.Value.IsLiteral ? node.Value : null;
					VisitGlobalFieldDeclaration(node.Decl, constInit, varType, (GlobalFieldDecl)sem);
				}
			} else {
				throw new NotImplementedException();
			}
			return;
		}

		Visit(node.Value);
		Debug.Assert(_values.Count == 1);
		var val = _values.Pop();
		var currBlock = _builder.Handle.InsertBlock;
		if (_currentFunc.EntryBasicBlock.LastInstruction.Handle != default) {
			_builder.Handle.PositionBefore(_currentFunc.EntryBasicBlock.LastInstruction);
		}
		var alloc = _builder.Handle.BuildAlloca(varType, $"localVar.{node.Decl.Name}");
		_builder.Handle.BuildStore(varType.Undef, alloc);
		_locals[node.Decl.Name] = alloc;
		_builder.Handle.PositionAtEnd(currBlock);
		_builder.Handle.BuildStore(val, alloc);
	}

	private void VisitGlobalConstDeclaration(VarDeclaration node, Expression value, LLVMTypeRef varType, GlobalFieldDecl sem)
	{
		Visit(value);
		Debug.Assert(_values.Count == 1);
		var val = _values.Pop();
		Debug.Assert(val.IsConstant);
		var field = _module.AddGlobal(varType, sem.FullName);
		field.Linkage = char.IsUpper(node.Name[0]) ? LLVMLinkage.LLVMExternalLinkage : LLVMLinkage.LLVMInternalLinkage;
		field.Initializer = val;
		field.IsGlobalConstant = true;
		_globals[sem.FullName] = field;
	}

	private void VisitGlobalFieldDeclaration(VarDeclaration node, Expression? constInit, LLVMTypeRef varType, GlobalFieldDecl sem)
	{
		var field = _module.AddGlobal(varType, sem.FullName);
		field.Linkage = char.IsUpper(node.Name[0]) ? LLVMLinkage.LLVMExternalLinkage : LLVMLinkage.LLVMInternalLinkage;
		if (constInit != null) {
			Visit(constInit);
			Debug.Assert(_values.Count == 1);
			var val = _values.Pop();
			field.Initializer = val;
		} else {
			field.Initializer = LLVMValueRef.CreateConstNull(varType);
		}
		_globals[sem.FullName] = field;
	}

	public override void VisitIfStatement(IfStatement node)
	{
		var parent = _builder.InsertBlock.Handle.Parent;
		var tBlock = parent.AppendBasicBlock("tBlock");
		var end = parent.AppendBasicBlock("ifEnd");
		var fBlock = node.FalseBlock != null ? parent.AppendBasicBlock("fBlock") : end;
		Debug.Assert(_values.Count == 0);
		Visit(node.Cond);
		Debug.Assert(_values.Count == 1);
		var cond = _values.Pop();
		_builder.Handle.BuildCondBr(cond, tBlock, fBlock);
		_builder.Handle.PositionAtEnd(tBlock);
		Visit(node.TrueBlock);
		BranchIfNecessary(node.TrueBlock, end);
		if (node.FalseBlock != null) {
			_builder.Handle.PositionAtEnd(fBlock);
			Visit(node.FalseBlock);
			BranchIfNecessary(node.FalseBlock, end);
		}
		_builder.Handle.PositionAtEnd(end);
		Debug.Assert(_values.Count == 0);
	}

	public override void VisitWhileStatement(WhileStatement node)
	{
		var parent = _builder.InsertBlock.Handle.Parent;
		var condEval = parent.AppendBasicBlock("cond");
		var loopBody = parent.AppendBasicBlock("while");
		var end = parent.AppendBasicBlock("whileEnd");
		_builder.Handle.BuildBr(condEval);
		_builder.Handle.PositionAtEnd(condEval);
		Debug.Assert(_values.Count == 0);
		Visit(node.Cond);
		Debug.Assert(_values.Count == 1);
		var cond = _values.Pop();
		_builder.Handle.BuildCondBr(cond, loopBody, end);
		_builder.Handle.PositionAtEnd(loopBody);
		Visit(node.Body);
		BranchIfNecessary(node.Body, condEval);
		_builder.Handle.PositionAtEnd(end);
		Debug.Assert(_values.Count == 0);
	}

	private void BranchIfNecessary(Statement[] block, LLVMBasicBlockRef end)
	{
		if (block.Length > 0 && block[^1] is not ReturnStatement) {
			_builder.Handle.BuildBr(end);
		}
	}

	public override void VisitReturnStatement(ReturnStatement node)
	{
		Debug.Assert(_values.Count == 0);
		Visit(node.Value);
		Debug.Assert(_values.Count == 1);
		var result = _values.Pop();
		_builder.Handle.BuildRet(result);
	}

	public override void VisitAssignStatement(AssignStatement node)
	{
		Debug.Assert(_values.Count == 0);
		Visit(node.Right);
		Debug.Assert(_values.Count == 1);
		var r = _values.Pop();
		_lValueVisitor.Visit(node.Left);
		Debug.Assert(_values.Count == 1);
		var l = _values.Pop();
		var lType = l.TypeOf;
		var value = node.Op switch {
			AssignOperator.Assign => r,
			AssignOperator.InPlaceAdd => _builder.Handle.BuildAdd(l, r, "InPlaceAdd"),
			AssignOperator.InPlaceSub => _builder.Handle.BuildSub(l, r, "InPlaceSub"),
			AssignOperator.InPlaceMul => _builder.Handle.BuildMul(l, r, "InPlaceMul"),
			AssignOperator.InPlaceDiv => throw new NotImplementedException(),
			AssignOperator.InPlaceIDiv => _builder.Handle.BuildSDiv(l, r, "InPlaceIDiv"),
			_ => throw new UnreachableException()
		};
		if (node.Left is Identifier id) {
			var pos = _locals[id.Name];
			_builder.Handle.BuildStore(value, pos);
		} else {
			_builder.Handle.BuildStore(value, l);
		}
	}

	public override void VisitExpressionStatement(ExpressionStatement node)
	{
		Debug.Assert(_values.Count == 0);
		base.VisitExpressionStatement(node);
		Debug.Assert(_values.Count == 1);
		_last = _values.Pop();
	}

	public override void VisitUnaryExpression(UnaryExpression node)
	{
		var count = _values.Count;
		Visit(node.Expr);
		Debug.Assert(_values.Count == count + 1);
		var value = _values.Pop();
		Debug.Assert(node.Operator == Ast.UnaryOperator.Minus);
		_values.Push(_builder.Handle.BuildNeg(value));
	}

	public override void VisitArithmeticExpression(ArithmeticExpression node)
	{
		var count = _values.Count;
		Visit(node.Left);
		Visit(node.Right);
		Debug.Assert(_values.Count == count + 2);
		var r = _values.Pop();
		var l = _values.Pop();
		var result = node.Operator switch {
			ArithmeticOperator.Add => _builder.Handle.BuildAdd(l, r, "add"),
			ArithmeticOperator.Subtract => _builder.Handle.BuildSub(l, r, "sub"),
			ArithmeticOperator.Multiply => _builder.Handle.BuildMul(l, r, "mul"),
			ArithmeticOperator.Modulus => _builder.Handle.BuildSRem(l, r, "mod"),
			_ => throw new NotImplementedException()
		};
		_values.Push(result);
	}

	public override void VisitComparisonExpression(ComparisonExpression node)
	{
		var count = _values.Count;
		Visit(node.Left);
		Visit(node.Right);
		Debug.Assert(_values.Count == count + 2);
		var r = _values.Pop();
		var l = _values.Pop();
		var pred = node.Operator switch {
			ComparisonOperator.Equals => LLVMIntPredicate.LLVMIntEQ,
			ComparisonOperator.NotEquals => LLVMIntPredicate.LLVMIntNE,
			ComparisonOperator.LessThan => LLVMIntPredicate.LLVMIntSLT,
			ComparisonOperator.GreaterThan => LLVMIntPredicate.LLVMIntSGT,
			ComparisonOperator.LessThanOrEqual => LLVMIntPredicate.LLVMIntSLE,
			ComparisonOperator.GreaterThanOrEqual => LLVMIntPredicate.LLVMIntSGE,
			_ => throw new NotImplementedException()
		};
		var result = _builder.Handle.BuildICmp(pred, l, r);
		_values.Push(result);
	}

	public override void VisitFunctionCallExpression(FunctionCallExpression node)
	{
		int selfCount = 0;
		if (node.Target is MemberIdentifier mi && node.Semantic!.SemanticType.HasFlag(SemanticType.HasSelf)) {
			selfCount = 1;
			Visit(mi.ParentExpr);
		}
		Visit(node.Args);
		Span<LLVMValueRef> args = stackalloc LLVMValueRef[node.Args.Length + selfCount];
		for (int i = 1; i <= args.Length; ++i) {
			args[^i] = _values.Pop();
		}
		if (node.Semantic!.SemanticType.HasFlag(SemanticType.Magic)) {
			if (node.Semantic is ImplicitConstructor ic) {
				VisitImplicitConstructorCall(node, ic);
			} else {
				VisitMagicFunctionCall(node, args);
				return;
			}
		}
		if (!_functions.TryGetValue(node.Semantic!.FullName, out var callee)) {
			throw new CompileError(node, $"GetNamedFunction failed for '{node.Semantic!.Name}'");
		}
		var isVoid = node.Target.Semantic!.Type == Types.Void;
		var result = _builder.Handle.BuildCall2(callee.Type, callee.Function, args, isVoid ? [] : node.Target.ToString().AsSpan());
		_values.Push(result);
	}

	private void VisitImplicitConstructorCall(FunctionCallExpression node, ImplicitConstructor ic)
	{
		if (!_functions.TryGetValue(ic.Name, out var data)) {
			var typ = (ICompositeType)ic.Type;
			var lTyp = LookupType(typ);
			var @params = typ.Fields.Select(f => LookupType(f.Type)).ToArray();
			var fType = LLVMTypeRef.CreateFunction(lTyp, @params, false);
			var func = _module.AddFunction(ic.Name, fType);
			var preserved = _builder.Handle.InsertBlock;
			try {
				_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
				var result = lTyp.Undef;
				for (uint i = 0; i < @params.Length; ++i) {
					result = _builder.Handle.BuildInsertValue(result, func.GetParam(i), i);
				}
				_builder.Handle.BuildRet(result);
			} finally {
				_builder.Handle.PositionAtEnd(preserved);
			}
			_functions.Add(ic.Name, (fType, func));
		}
	}

	private void VisitMagicFunctionCall(FunctionCallExpression node, Span<LLVMValueRef> args)
	{
		switch (node.Semantic!.FullName) {
			case "ord":
				_values.Push(_builder.Handle.BuildZExt(args[0], LookupType(node.Semantic!.Type), "ord"));
				break;
			case "StrToPtr":
				_values.Push(_builder.Handle.BuildStructGEP2(_builtinTypes["string"], args[0], 1, "strData"));
				break;
			case "span$ToPtr":
				var parent = ((MemberIdentifier)node.Target).ParentExpr;
				Visit(parent);
				var pVal = _values.Pop();
				_values.Push(_builder.Handle.BuildExtractValue(pVal, 0, "spanData"));
				break;
			case "ptr$AsSpan":
				parent = ((MemberIdentifier)node.Target).ParentExpr;
				Visit(parent);
				pVal = _values.Pop();
				var spanType = BuildSpanType((SpanType)node.Semantic.Type);
				var result = spanType.Undef;
				result = _builder.Handle.BuildInsertValue(result, pVal, 0, "span.data");
				result = _builder.Handle.BuildInsertValue(result, args[0], 1, "span.len");
				_values.Push(result);
				break;
			default:
				throw new NotImplementedException();
		}
	}

	private void VisitMagicPropertyCall(MagicProperty mp, LLVMValueRef parent)
	{
		switch (mp.FullName) {
			case "span$Length":
				_values.Push(_builder.Handle.BuildExtractValue(parent, 1, "spanLength"));
				break;
			default:
				throw new NotImplementedException();
		}
	}

	private void VisitFieldDecl(FieldDecl fd, LLVMValueRef parent) 
		=> _values.Push(_builder.Handle.BuildExtractValue(parent, (uint)fd.Index, $"{parent.Name}.{fd.Name}"));

	public override void VisitMatchExpression(MatchExpression node)
	{
		var count = _values.Count;
		Visit(node.Value);
		Debug.Assert(_values.Count == count + 1);
		var value = _values.Pop();
		var parent = _builder.InsertBlock.Handle.Parent;
		var cases = node.Cases.Select(_ => parent.AppendBasicBlock("matchCase")).ToArray();
		var end = parent.AppendBasicBlock("matchMerge");
		var switchCount = node.Cases.Where(c => c.Cases != null).Sum(c => c.Cases!.Length);
		var switchInst = _builder.Handle.BuildSwitch(value, cases[^1], (uint)switchCount);
		for (int i = 0; i < node.Cases.Length - 1; ++i) {
			foreach (var match in node.Cases[i].Cases!) {
				Visit(match);
				switchInst.AddCase(_values.Pop(), cases[i]);
			}
		}
		_builder.Handle.PositionAtEnd(end);
		var result = _builder.Handle.BuildPhi(value.TypeOf);
		for (int i = 0; i < node.Cases.Length; ++i) {
			_builder.Handle.PositionAtEnd(cases[i]);
			Visit(node.Cases[i].Value);
			_builder.Handle.BuildBr(end);
			result.AddIncoming([_values.Pop()], [cases[i]], 1);
		}
		_builder.Handle.PositionAtEnd(end);
		_values.Push(result);
	}

	public override void VisitTernaryExpression(TernaryExpression node)
	{
		var count = _values.Count;
		Visit(node.Cond);
		Debug.Assert(_values.Count == count + 1);
		var condition = _values.Pop();
		var parent = _builder.InsertBlock.Handle.Parent;
		LLVMBasicBlockRef[] cases = [parent.AppendBasicBlock("thenCase"), parent.AppendBasicBlock("elseCase")];
		var end = parent.AppendBasicBlock("ternaryMerge");
		_builder.Handle.BuildCondBr(condition, cases[0], cases[1]);
		// evaluate the cases backwards because of stack behavior
		_builder.Handle.PositionAtEnd(cases[1]);
		Visit(node.Right);
		_builder.Handle.BuildBr(end);
		_builder.Handle.PositionAtEnd(cases[0]);
		Visit(node.Left);
		_builder.Handle.BuildBr(end);
		_builder.Handle.PositionAtEnd(end);
		var result = _builder.Handle.BuildPhi(LookupType(node.Semantic!.Type));
		result.AddIncoming([_values.Pop()], [cases[0]], 1);
		result.AddIncoming([_values.Pop()], [cases[1]], 1);
		_values.Push(result);
	}

	public override void VisitIndexingExpression(IndexingExpression node)
	{
		var count = _values.Count;
		Visit(node.Expr);
		Debug.Assert(_values.Count == count + 1);
		var expr = _values.Pop();
		var (span, elType) = node.Expr.Semantic!.Type switch {
			SpanType st => (_builder.Handle.BuildExtractValue(expr, 0, "spanPtr"), LookupType(st.BaseType)),
			TypeSystem.ArrayType at => (_builder.Handle.BuildExtractValue(expr, 0, "spanPtr"), LookupType(at.BaseType)),
			_ => throw new UnreachableException(),
		};

		//TODO: support multidimensional indexing
		var idxExpr = node.Indices[0];
		if (idxExpr is SliceExpression s) {
			var len = _builder.Handle.BuildExtractValue(expr, 1, "spanLen");
			VisitSlicing(span, len, LookupType(node.Expr.Semantic!.Type), elType, s);
		} else {
			VisitIndexing(span, elType, idxExpr);
		}
	}

	private void VisitIndexing(LLVMValueRef expr, LLVMTypeRef elType, Expression idxExpr)
	{
		var count = _values.Count;
		Visit(idxExpr);
		Debug.Assert(_values.Count == count + 1);
		var idx = _values.Pop();
		var result = _builder.Handle.BuildLoad2(elType, _builder.Handle.BuildInBoundsGEP2(elType, expr, [idx], "indexPtr".AsSpan()), "index");
		_values.Push(result);
	}

	private void VisitSlicing(LLVMValueRef data, LLVMValueRef len, LLVMTypeRef spanType, LLVMTypeRef elType, SliceExpression s)
	{
		LLVMValueRef start, end;
		if (s.End == null) {
			end = len;
		} else {
			var count = _values.Count;
			Visit(s.End);
			Debug.Assert(_values.Count == count + 1);
			end = _values.Pop();
		}
		if (s.Start == null) {
			start = LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 0);
		} else {
			var count = _values.Count;
			Visit(s.Start);
			Debug.Assert(_values.Count == count + 1);
			start = _values.Pop();
			end = _builder.Handle.BuildSub(end, start);
		}
		var newData = _builder.Handle.BuildGEP2(elType, data, new[] { start }, "slice.ptr");
		var result = spanType.Undef;
		result = _builder.Handle.BuildInsertValue(result, newData, 0, "slice.data");
		result = _builder.Handle.BuildInsertValue(result, end, 1, "slice.len");
		_values.Push(result);
	}

	public override void VisitSliceExpression(SliceExpression node) => throw new UnreachableException("Should be handled in parent node");

	public override void VisitCastExpression(CastExpression node)
	{
		var count = _values.Count;
		Visit(node.Value);
		Debug.Assert(_values.Count == count + 1);
		var expr = _values.Pop();
		var typ = LookupType(node.Semantic!.Type);
		var result = BuildCast(node.Value.Semantic!.Type, node.Semantic.Type, expr, typ);
		//var result = _builder.Handle.BuildIntCast(expr, typ, "cast");
		_values.Push(result);
	}

	private LLVMValueRef BuildCast(IType type, IType targetType, LLVMValueRef expr, LLVMTypeRef typ)
	{
		if (type == targetType) {
			return expr;
		}
		if (type is PrimitiveType && targetType is PrimitiveType) {
			return _builder.Handle.BuildIntCast(expr, typ, "cast");
		}
		if (type is StringType && targetType is SpanType) {
			var strLenPtr = _builder.Handle.BuildStructGEP2(_builtinTypes["string"], expr, 0, "strLen");
			var strDataPtr = _builder.Handle.BuildStructGEP2(_builtinTypes["string"], expr, 1, "strData");
			var span = typ.Undef;
			span = _builder.Handle.BuildInsertValue(span, strDataPtr, 0, "spanData");
			span = _builder.Handle.BuildInsertValue(span, _builder.Handle.BuildLoad2(_context.Handle.Int32Type, strLenPtr), 1, "spanLen");
			return span;
		}
		if (type is TypeSystem.ArrayType && targetType is SpanType) {
			return expr;
		}
		throw new NotImplementedException();
	}

	public override void VisitRefExpression(RefExpression node)
	{
		switch (node.Expr.Semantic) {
			case VariableDecl vd:
				_values.Push(_locals[vd.Name]);
				break;
			default:
				throw new NotImplementedException();
		}
	}

	public override void VisitNewArrayExpression(NewArrayExpression node)
	{
		var typeLen = LLVMValueRef.CreateConstInt(_context.Handle.Int64Type, SizeOf(LookupType(node.ArrayType)));
		var count = _values.Count;
		Visit(node.Size);
		Debug.Assert(_values.Count == count + 1);
		var arrayLen = _values.Pop();
		var arrayLen64 = _builder.Handle.BuildIntCast(arrayLen, _context.Handle.Int64Type);
		var calloc = _functions["calloc"];
		var call = _builder.Handle.BuildCall2(calloc.Type, calloc.Function, [typeLen, arrayLen64]);
		var cast = _builder.Handle.BuildBitCast(call, LLVMTypeRef.CreatePointer(LookupType(node.ArrayType), 0));
		var spanType = LookupType(node.Semantic!.Type);
		var span = spanType.Undef;
		span = _builder.Handle.BuildInsertValue(span, cast, 0);
		span = _builder.Handle.BuildInsertValue(span, arrayLen, 1);
		_values.Push(span);
	}

	public override void VisitNewObjExpression(NewObjExpression node)
	{
		if (node.ObjectType.Semantic!.Type == Types.String) {
			VisitNewString(node);
		} else {
			throw new NotImplementedException();
		}
	}

	private void VisitNewString(NewObjExpression node)
	{
		var count = _values.Count;
		Visit(node.Args[0]);
		Debug.Assert(_values.Count == count + 1);
		var span = _values.Pop();
		var len = _builder.Handle.BuildExtractValue(span, 1, "spanLen");
		var typeLen = _builder.Handle.BuildAdd(len, LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 5));
		var typeLen64 = _builder.Handle.BuildIntCast(typeLen, _context.Handle.Int64Type);
		var malloc = _functions["malloc"];
		var strPtr = _builder.Handle.BuildCall2(malloc.Type, malloc.Function, [typeLen64]);
		var stringType = _builtinTypes["string"];
		var lenPtr = _builder.Handle.BuildGEP2(stringType, strPtr, [
				LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 0, false),
				LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 0, false)
			],
			"lenPtr".AsSpan());
		_builder.Handle.BuildStore(len, lenPtr);
		var dataPtrPtr = _builder.Handle.BuildGEP2(stringType, strPtr, [
				LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 0, false),
				LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 1, false)
			],
			"dataPtr".AsSpan());
		var memcpy = _functions["memcpy"];
		var data = _builder.Handle.BuildExtractValue(span, 0, "data"); // i8*
		//var len64 = _builder.Handle.BuildZExt(len, _context.Handle.Int64Type, "len64");  
		_builder.Handle.BuildCall2(memcpy.Type, memcpy.Function, [
				dataPtrPtr, // dest
				data,        // src
				len//len64,       // size
				//LLVMValueRef.CreateConstInt(_context.Handle.Int1Type, 0, false)
			],
			[]
		);
		var endPtr = _builder.Handle.BuildGEP2(_context.Handle.Int8Type, dataPtrPtr, [len], "end_ptr".AsSpan() );
		_builder.Handle.BuildStore(LLVMValueRef.CreateConstInt(LLVMTypeRef.Int8, 0, false), endPtr);
		_values.Push(strPtr);
	}

	public override void VisitMemberIdentifier(MemberIdentifier node)
	{
		var count = _values.Count;
		Visit(node.ParentExpr);
		Debug.Assert(_values.Count == count + 1);
		VisitIdentifier(node);
	}

	public override void VisitIdentifier(Identifier node)
	{
		switch (node.Semantic) {
			case null:
				break;
			case ParamDef:
			case VariableDecl:
				var ptr = _locals[node.Semantic.Name];
				_values.Push(_builder.Handle.BuildLoad2(LookupType(node.Semantic.Type), ptr, "var_" + node.Semantic.Name));
				break;
			case GlobalFieldDecl gf:
				ptr = _globals[node.Semantic.FullName];
				_values.Push(_builder.Handle.BuildLoad2(LookupType(node.Semantic.Type), ptr, "var_" + node.Semantic.Name));
				break;
			case Semantics.Module:
			case StructDecl:
				break;
			case MagicProperty mp:
				var parent = _values.Pop();
				VisitMagicPropertyCall(mp, parent);
				break;
			case FieldDecl fd:
				parent = _values.Pop();
				VisitFieldDecl(fd, parent);
				break;
			default:
				throw new CompileError(node, "Unknown Identifier semantic type");
		}
	}

	public override void VisitCharLiteralExpression(CharLiteralExpression node)
		=> _values.Push(LLVMValueRef.CreateConstInt(_context.Handle.Int8Type, node.Value));

	public override void VisitStringLiteralExpression(StringLiteralExpression node)
	{
		var value = node.Value;
		var strBytes = Encoding.UTF8.GetBytes(value);
		var arrayType = LLVMTypeRef.CreateArray(_context.Handle.Int8Type, (uint)strBytes.Length + 1);
		var literalStructType = _context.Handle.GetStructType([_context.Handle.Int32Type, arrayType], false);
		var lenConst = LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, (uint)strBytes.Length, false);
		var pchar = _context.Handle.GetConstString(value, false);
		var structConst = LLVMValueRef.CreateConstNamedStruct(literalStructType, [lenConst, pchar]);
		var result = _module.AddGlobal(literalStructType, "str");

		result.Initializer = structConst;
		result.Linkage = LLVMLinkage.LLVMPrivateLinkage;
		result.IsGlobalConstant = true;
		_values.Push(result);
	}

	public override void VisitIntegerLiteralExpression(IntegerLiteralExpression node)
		=> _values.Push(LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, (ulong)node.Value));

	public override void VisitNullLiteralExpression(NullLiteralExpression node)
		=> _values.Push(LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(_context.Handle.VoidType, 0)));

	public override void VisitSelfLiteralExpression(SelfLiteralExpression node) => _values.Push(_currentFunc.FirstParam);
}
