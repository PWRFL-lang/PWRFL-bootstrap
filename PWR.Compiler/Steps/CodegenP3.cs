using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

public unsafe partial class CodegenP3(LLVMContext context, LLVMModuleRef module, string filename, bool isLibrary, bool debugInfo = false) : VisitorCompileStep
{
	private readonly LLVMContext _context = context;
	private readonly LLVMModuleRef _module = module;
	private readonly string _filename = filename;
	private readonly bool _isLibrary = isLibrary;
	private readonly bool _debugInfo = debugInfo; 
	private LLVMDIBuilderRef _diBuilder = null!;
	private LLVMMetadataRef _diCompileUnit;
	private LLVMMetadataRef _diSubprogram; // subprogram of the function being emitted (default when in synthetic code)
	private readonly Dictionary<string, LLVMMetadataRef> _diFiles = [];
	private readonly Dictionary<IType, LLVMMetadataRef> _diTypes = [];
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
	private bool _isCtor;

	public override Project Run(Project tree)
	{
		_builder = new(_context);
		_lValueVisitor = new(_module, _builder.Handle, _values, _locals, _globals, LookupType, () => _currentFunc, () => _isCtor);

		LoadStdlib();
		if (_debugInfo) {
			InitDebugInfo();
		}
		LoadImports(tree);
		WriteMetadata(tree);

		var result = base.Run(tree);
		if (!_isLibrary) {
			BuildProgramInit(tree.Imports);
		}
		if (_debugInfo) {
			LLVM.DIBuilderFinalize(_diBuilder);
		}
		return result;
	}

	private void InitDebugInfo()
	{
		_diBuilder = _module.CreateDIBuilder();
		_module.AddModuleFlag(
			"Debug Info Version",
			LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning,
			LLVM.ValueAsMetadata(LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 3)));
		_module.AddModuleFlag(
			"CodeView",
			LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning,
			LLVM.ValueAsMetadata(LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 1)));
		_diCompileUnit = _diBuilder.CreateCompileUnit(
			LLVMDWARFSourceLanguage.LLVMDWARFSourceLanguageC,
			GetDIFile(_filename), "PWRFL compiler", 0, "", 0, "",
			LLVMDWARFEmissionKind.LLVMDWARFEmissionFull, 0, 0, 0, "", "");
	}

	private LLVMMetadataRef GetDIFile(string filename)
	{
		if (string.IsNullOrEmpty(filename)) {
			filename = _filename;
		}
		if (!_diFiles.TryGetValue(filename, out var file)) {
			file = _diBuilder.CreateFile(Path.GetFileName(filename), Path.GetDirectoryName(filename) ?? ".");
			_diFiles[filename] = file;
		}
		return file;
	}

	// Attach a DISubprogram to a real (source-backed) function and make it the current debug
	// scope. Synthetic functions instead call ClearDebugScope
	private void BeginDebugFunction(LLVMValueRef func, string name, Position pos)
	{
		if (!_debugInfo) {
			return;
		}
		var file = GetDIFile(pos.File);
		var line = (uint)Math.Max(pos.Line, 1);
		var subType = _diBuilder.CreateSubroutineType(file, [], LLVMDIFlags.LLVMDIFlagZero);
		_diSubprogram = _diBuilder.CreateFunction(_diCompileUnit, name, func.Name, file, line, subType,
			IsLocalToUnit: 0, IsDefinition: 1, ScopeLine: line, LLVMDIFlags.LLVMDIFlagZero, IsOptimized: 0);
		LLVM.SetSubprogram(func, _diSubprogram);
		SetLocation(pos);
	}

	// Synthetic functions (runtimeMain, module/program init, implicit ctors) carry no debug info,
	// so their instructions must have no location
	private void ClearDebugScope()
	{
		if (!_debugInfo) {
			return;
		}
		_diSubprogram = default;
		LLVM.SetCurrentDebugLocation2(_builder.Handle, default);
	}

	private void SetLocation(Position pos)
	{
		if (!_debugInfo || _diSubprogram.Handle == default) {
			return;
		}
		var loc = _context.Handle.CreateDebugLocation((uint)Math.Max(pos.Line, 1), (uint)(pos.Column + 1), _diSubprogram, default);
		LLVM.SetCurrentDebugLocation2(_builder.Handle, loc);
	}

	// Statement granularity locations: every statement in a body/block flows through here, so the
	// instructions it emits inherit its source position
	public override void Visit<T>(T[]? list)
	{
		if (typeof(T).IsAssignableTo(typeof(Statement))) {
			if (_debugInfo && _diSubprogram.Handle != default && list != null) {
				foreach (var node in list) {
					SetLocation(node.Position);
					node.Accept(this);
				}
				return;
			}
		}
		base.Visit(list);
	}

	// Emit a llvm.dbg.declare associating an alloca with a source variable so the debugger can
	// inspect it. paramIndex > 0 marks a function parameter
	private void DeclareLocal(LLVMValueRef storage, string name, IType type, Position pos, int paramindex)
	{
		if (!_debugInfo || _diSubprogram.Handle == default) {
			return;
		}
		var diType = GetDIType(type);
		if (diType.Handle == default) {
			return;
		}
		var file = GetDIFile(pos.File);
		var line = (uint)Math.Min(pos.Line, 1);
		using var nameM = new MarshaledString(name);
		var varInfo = paramindex > 0
			? LLVM.DIBuilderCreateParameterVariable(_diBuilder, _diSubprogram, nameM, (nuint)nameM.Length, (uint)paramindex, file, line, diType, 1, LLVMDIFlags.LLVMDIFlagZero)
			: LLVM.DIBuilderCreateAutoVariable(_diBuilder, _diSubprogram, nameM, (nuint)nameM.Length, file, line, diType, 1, LLVMDIFlags.LLVMDIFlagZero, 0);
		var expr = LLVM.DIBuilderCreateExpression(_diBuilder, null, 0);
		var loc = _context.Handle.CreateDebugLocation(line, (uint)(pos.Column + 1), _diSubprogram, default);
		LLVM.DIBuilderInsertDeclareRecordAtEnd(_diBuilder, storage, varInfo, expr, loc, _builder.Handle.InsertBlock);
	}

	private ulong SizeInBits(LLVMTypeRef t) => SizeOf(t) * 8;

	private ulong OffsetInBits(LLVMTypeRef structType, uint index)
		=> LLVM.OffsetOfElement(new DataLayout(_module.DataLayout).Handle, structType, index) * 8;

	private LLVMMetadataRef GetDIType(IType? type)
	{
		if (type == null || type == Types.Void) {
			return default;
		}
		if (!_diTypes.TryGetValue(type, out var result)) {
			result = type switch {
				PrimitiveType pt => BasicDIType(pt),
				NilableType nt => GetDIType(nt.BaseType),
				RefType rt => PointerDIType(GetDIType(rt.BaseType), type.Name),
				TypeSystem.PointerType => PointerDIType(default, type.Name),
				StringType => PointerDIType(StringStructDIType(), "string"),
				SpanType st => SpanDIType(type, st.BaseType),
				TypeSystem.ArrayType at => SpanDIType(type, at.BaseType),
				InternalStruct ist => StructDIType(type, ist),
				InlineArrayType ia => InlineArrayDIType(ia),
				_ => throw new NotImplementedException(),
			};
			_diTypes[type] = result;
		}
		return result;
	}

	// DWARF base-type encodings (DWARF 5 spec, table 7:11).  LLVM-C takes a raw unsigned here, so
	// these aren't available as an LLVMSharp enum.
	private const uint DW_ATE_BOOLEAN = 0x02;
	private const uint DW_ATE_SIGNED = 0x05;
	private const uint DW_ATE_UNSIGNED = 0x07;
	private const uint DW_ATE_UNSIGNED_CHAR = 0x07;
	private const uint DW_TAG_STRUCTURE_TYPE = 0x13;

	private LLVMMetadataRef BasicDIType(PrimitiveType pt)
	{
		uint encoding = pt.Name switch {
			"bool" => DW_ATE_BOOLEAN,
			"char" => DW_ATE_UNSIGNED_CHAR,
			"byte" => DW_ATE_UNSIGNED,
			"int" or "long" => DW_ATE_SIGNED,
			_ => throw new NotImplementedException()
		};
		using var name = new MarshaledString(pt.Name);
		return LLVM.DIBuilderCreateBasicType(_diBuilder, name, (nuint)name.Length, SizeInBits(pt.Type), encoding, LLVMDIFlags.LLVMDIFlagZero);
	}

	private const int INT_BIT_SIZE = 32;
	private const int POINTER_BIT_SIZE = 64;

	private LLVMMetadataRef PointerDIType(LLVMMetadataRef baseType, string typeName)
	{
		using var name = new MarshaledString(typeName);
		return LLVM.DIBuilderCreatePointerType(_diBuilder, baseType, POINTER_BIT_SIZE, 0, 0, name, (uint)name.Length);
	}

	private LLVMMetadataRef StringStructDIType()
	{
		var llvmType = _builtinTypes["string"];
		var file = GetDIFile(_filename);
		var members = stackalloc LLVMMetadataRef[2];
		using (var len = new MarshaledString("len")) {
			members[0] = LLVM.DIBuilderCreateMemberType(_diBuilder, default, len, (nuint)len.Length,
				file, 0, 32, 0, OffsetInBits(llvmType, 0), LLVMDIFlags.LLVMDIFlagZero, GetDIType(Types.Int32));
		}
		var subs = stackalloc LLVMMetadataRef[1];
		subs[0] = LLVM.DIBuilderGetOrCreateSubrange(_diBuilder, 0, 0);
		var charArray = LLVM.DIBuilderCreateArrayType(_diBuilder, 0, 0, GetDIType(Types.Char), (LLVMOpaqueMetadata**)subs, 1);
		using (var bytes = new MarshaledString("bytes")) {
			members[0] = LLVM.DIBuilderCreateMemberType(_diBuilder, default, bytes, (nuint)bytes.Length,
				file, 0, 0, 0, OffsetInBits(llvmType, 1), LLVMDIFlags.LLVMDIFlagZero, charArray);
		}
		using var str = new MarshaledString("string");
		return LLVM.DIBuilderCreateStructType(_diBuilder, default, str, (nuint)str.Length,
			file, 0, SizeInBits(llvmType), 0, LLVMDIFlags.LLVMDIFlagZero, default,
			(LLVMOpaqueMetadata**)members, 2, 0, default, null, 0);
	}

	private LLVMMetadataRef SpanDIType(IType type, IType elemType)
	{
		var llvmType = LookupType(type);
		var file = GetDIFile(_filename);
		var ptr = LLVM.DIBuilderCreatePointerType(_diBuilder, GetDIType(elemType), POINTER_BIT_SIZE, 0, 0, null, 0);
		var members = stackalloc LLVMMetadataRef[2];
		using (var dn = new MarshaledString("data")) {
			members[0] = LLVM.DIBuilderCreateMemberType(_diBuilder, default, dn, (nuint)dn.Length, file, 0,
				POINTER_BIT_SIZE, 0, OffsetInBits(llvmType, 0), LLVMDIFlags.LLVMDIFlagZero, ptr);
		}
		using (var ln = new MarshaledString("data")) {
			members[1] = LLVM.DIBuilderCreateMemberType(_diBuilder, default, ln, (nuint)ln.Length, file, 0,
				INT_BIT_SIZE, 0, OffsetInBits(llvmType, 1), LLVMDIFlags.LLVMDIFlagZero, GetDIType(Types.Int32));
		}
		using var sn = new MarshaledString(type.Name);
		return LLVM.DIBuilderCreateStructType(_diBuilder, _diCompileUnit, sn, (nuint)sn.Length, file, 0,
			SizeInBits(llvmType), 0, LLVMDIFlags.LLVMDIFlagZero, default,
			(LLVMOpaqueMetadata**)members, 2, 0, default, null, 0);
	}

	private LLVMMetadataRef StructDIType(IType type, InternalStruct ist)
	{
		var llvmType = LookupType(type);
		var pos = ist.Decl.Position;
		var file = GetDIFile(pos.File);
		var line = (uint)Math.Max(pos.Line, 1);
		using var name = new MarshaledString(ist.Name);
		// temp forward-declared type for use by recursive types.
		var fwd = LLVM.DIBuilderCreateReplaceableCompositeType(_diBuilder, DW_TAG_STRUCTURE_TYPE,
			name, (nuint)name.Length, _diCompileUnit, file, line, 0, SizeInBits(llvmType), 0,
			LLVMDIFlags.LLVMDIFlagZero, name, (nuint)name.Length);
		_diTypes[type] = fwd;

		var members = stackalloc LLVMMetadataRef[ist.Fields.Length];
		for (int i = 0; i < ist.Fields.Length; ++i) {
			var field = ist.Fields[i];
			using var fName = new MarshaledString(field.Name);
			members[i] = (LLVMMetadataRef)LLVM.DIBuilderCreateMemberType(_diBuilder, fwd, fName, (nuint)fName.Length,
				file, line, SizeInBits(LookupType(field.Type)), 0, OffsetInBits(llvmType, (uint)i),
				LLVMDIFlags.LLVMDIFlagZero, GetDIType(field.Type));
		}
		var result = LLVM.DIBuilderCreateStructType(_diBuilder, _diCompileUnit, name, (nuint)name.Length,
			file, line, SizeInBits(llvmType), 0, LLVMDIFlags.LLVMDIFlagZero, default, (LLVMOpaqueMetadata**)members,
			(uint)ist.Fields.Length, 0, default, name, (nuint)name.Length);
		LLVM.MetadataReplaceAllUsesWith(fwd, result);
		return result;
	}

	private LLVMMetadataRef InlineArrayDIType(InlineArrayType ia)
	{
		var llvmType = LookupType(ia);
		var count = (long)(SizeOf(llvmType) / SizeOf(LookupType(ia.BaseType)));
		var subs = stackalloc LLVMMetadataRef[1];
		subs[0] = LLVM.DIBuilderGetOrCreateSubrange(_diBuilder, 0, count);
		return LLVM.DIBuilderCreateArrayType(_diBuilder, SizeInBits(llvmType), 0,
			GetDIType(ia.BaseType), (LLVMOpaqueMetadata**)subs, 1);
	}

	private void LoadStdlib()
	{
		_mainType = LLVMTypeRef.CreateFunction(_context.Handle.VoidType, []);

		var bytesType = LLVMTypeRef.CreateArray(_context.Handle.Int8Type, 0);
		Debug.Assert(bytesType.Context.Handle == _module.Context.Handle);
		var stringType = _context.Handle.CreateNamedStruct("string");
		stringType.StructSetBody([_context.Handle.Int32Type, bytesType ], false );
		Debug.Assert(stringType.Context.Handle == _module.Context.Handle);
		var strPtr = LLVMTypeRef.CreatePointer(stringType, 0);
		Debug.Assert(strPtr.Context.Handle == _module.Context.Handle);
		_builtinTypes.Add("string", stringType);

		_builtinTypes.Add("ptr", LLVMTypeRef.CreatePointer(_context.Handle.VoidType, 0));
	}

	private readonly HashSet<string> _forwardDeclared = [];

	private (LLVMTypeRef Type, LLVMValueRef Function) GetAllocFunc()
	{
		const string name = "Memory$Alloc";
		if (!_functions.TryGetValue(name, out var result)) {
			var type = LLVMTypeRef.CreateFunction(LookupType(SpanType.Create(Types.Byte)), [_context.Handle.Int32Type], false);
			result = (type, _module.AddFunction(name, type));
			_functions.Add(name, result);
			_forwardDeclared.Add(name);
		}
		return result;
	}

	private (LLVMTypeRef Type, LLVMValueRef Function) GetReallocFunc()
	{
		const string name = "Memory$Realloc";
		if (!_functions.TryGetValue(name, out var result))
		{
			var byteSpan = LookupType(SpanType.Create(Types.Byte));
			var type = LLVMTypeRef.CreateFunction(byteSpan, [byteSpan, _context.Handle.Int32Type], false);
			result = (type, _module.AddFunction(name, type));
			_functions.Add(name, result);
			_forwardDeclared.Add(name);
		}
		return result;
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
		ClearDebugScope();
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
			ClearDebugScope();
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
			BeginDebugFunction(main, "main", node.Position);
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
			_customTypes.Add(typ.StructName, typ);
			typ.StructSetBody([.. fields.Select(f => LookupType(f.Semantic!.Type))], false);
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
		ClearDebugScope();
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
		LLVMValueRef func;
		if (_forwardDeclared.Remove(name)) {
			func = _functions[name].Function; 
		} else {
			func = _module.AddFunction(name, funcType);
			_functions.Add(name, (funcType, func));
		}
		_currentFunc = func;
		_locals.Clear();

		_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
		BeginDebugFunction(func, node.Name.Name, node.Position);
		for (int i = 0; i < node.Parameters.Length; ++i) {
			var param = func.GetParam((uint)i);
			param.Name = node.Parameters[i].Name.Name;
			var alloc = _builder.Handle.BuildAlloca(param.TypeOf, $"param${param.Name}");
			_locals[param.Name] = alloc;
			_builder.Handle.BuildStore(param, alloc);
			DeclareLocal(alloc, param.Name, node.Parameters[i].Semantic!.Type, node.Parameters[i].Position, i + 1);
		}

		if (node.IsConstructor) {
			_isCtor = true;
			var selfType = LookupType(node.Semantic!.Type);
			var selfPtr = _builder.Handle.BuildAlloca(selfType, "self");
			_locals.Add("self", selfPtr);
			_builder.Handle.BuildStore(LLVM.ConstNull(selfType), selfPtr);
		}
		Visit(node.Body);
		if (_isCtor) {
			_builder.Handle.BuildRet(_builder.Handle.BuildLoad2(LookupType(node.Semantic!.Type), _locals["self"], "result"));
		} else if (node.Body.Length == 0 || node.Body[^1] is not ReturnStatement) {
			if ((node.ReturnType?.Semantic?.Type ?? Types.Void) == Types.Void) {
				_builder.Handle.BuildRetVoid();
			} else {
				_builder.CreateUnreachable();
			}
		}
		_isCtor = false;
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

	private LLVMTypeRef BuildFuncType(IFunction func)
		=> LLVMTypeRef.CreateFunction(
			LookupType(((ISemantic)func).Type),
			[.. func.Args.Select(p => LookupType(p.ParamType))],
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
		InlineArrayType ia => BuildInlineArrayType(LookupType(ia.BaseType), ia.BaseType.Name, ia.Size!),
		InternalPrimitiveType => LLVMTypeRef.CreatePointer(_builtinTypes[type.Name], 0),
		RefType rt => LLVMTypeRef.CreatePointer(LookupType(rt.BaseType), 0),
		InternalStruct ist => _customTypes[ist.Name],
		InternalClass icl => _customTypes[icl.Name],
		NilableType nt => LookupType(nt.BaseType),
		_ => throw new NotImplementedException()
	};

	private LLVMTypeRef BuildInlineArrayType(LLVMTypeRef baseType, string name, Expression size)
	{
		var sizeVal = size switch {
			IntegerLiteralExpression il => il.Value,
			LiteralExpression => throw new CompileError(size, "Fixed-size array must have an integer size"),
			_ => throw new CompileError(size, "Fixed-size array must have a constant size"),
		};
		return LLVMTypeRef.CreateArray(baseType, (uint)sizeVal);
	}

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
		DeclareLocal(alloc, node.Decl.Name, sem.Type, node.Position, 0);
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

	private void BranchIfNecessary(Block block, LLVMBasicBlockRef end)
	{
		if (block.Body.Length > 0 && block.Body[^1] is not ReturnStatement) {
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
		var cf = _currentFunc.ToString();
		_lValueVisitor.Visit(node.Left);
		Debug.Assert(_values.Count == 1);
		var l = _values.Pop();
		var lType = l.TypeOf;
		var lValue = node.Left is MemberIdentifier ? _builder.Handle.BuildLoad2(LookupType(node.Left.Semantic!.Type), l) : l;
		var value = node.Op switch {
			AssignOperator.Assign => r,
			AssignOperator.InPlaceAdd => _builder.Handle.BuildAdd(lValue, r, "InPlaceAdd"),
			AssignOperator.InPlaceSub => _builder.Handle.BuildSub(lValue, r, "InPlaceSub"),
			AssignOperator.InPlaceMul => _builder.Handle.BuildMul(lValue, r, "InPlaceMul"),
			AssignOperator.InPlaceDiv => throw new NotImplementedException(),
			AssignOperator.InPlaceIDiv => _builder.Handle.BuildSDiv(lValue, r, "InPlaceIDiv"),
			_ => throw new UnreachableException()
		};
		if (node.Left is Identifier id && id.Type == NodeType.Identifier) {
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
			ArithmeticOperator.Divide => _builder.Handle.BuildFDiv(l, r, "fdiv"),
			ArithmeticOperator.IDivide => _builder.Handle.BuildSDiv(l, r, "idiv"),
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
			var preservedLoc = _debugInfo ? LLVM.GetCurrentDebugLocation2(_builder.Handle) : default;
			if (_debugInfo) {
				LLVM.SetCurrentDebugLocation2(_builder.Handle, default);
			}
			try {
				_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
				var result = lTyp.Undef;
				for (uint i = 0; i < @params.Length; ++i) {
					result = _builder.Handle.BuildInsertValue(result, func.GetParam(i), i);
				}
				_builder.Handle.BuildRet(result);
			} finally {
				_builder.Handle.PositionAtEnd(preserved);
				if (_debugInfo) {
					LLVM.SetCurrentDebugLocation2(_builder.Handle, preservedLoc);
				}
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
			case "ref$AsSpan":
				parent = ((MemberIdentifier)node.Target).ParentExpr;
				Visit(parent);
				pVal = _values.Pop();
				var spanType = BuildSpanType((SpanType)node.Semantic.Type);
				var result = spanType.Undef;
				result = _builder.Handle.BuildInsertValue(result, pVal, 0, "span.data");
				result = _builder.Handle.BuildInsertValue(result, args[0], 1, "span.len");
				_values.Push(result);
				break;
			case "$print":
				var (funcType, func) = _functions["Console$PrintLn"];
				_values.Push(_builder.Handle.BuildCall2(funcType, func, args, ""));
				break;
			case "arr$Resize":
				VisitArrayResize(node, args[0]);
				break;
			default:
				throw new NotImplementedException();
		}
	}

	private void VisitArrayResize(FunctionCallExpression node, LLVMValueRef newLen)
	{
		var target = (MemberIdentifier)node.Target;
		var arrType = (TypeSystem.ArrayType)target.ParentExpr.Semantic!.Type;
		var arrSpanType = LookupType(arrType);
		var elemSize = LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, SizeOf(LookupType(arrType.BaseType)));

		_lValueVisitor.Visit(target.ParentExpr);
		var arrPtr = _values.Pop();
		var oldSpan = _builder.Handle.BuildLoad2(arrSpanType, arrPtr, "oldArr");
		var oldData = _builder.Handle.BuildExtractValue(oldSpan, 0, "oldData");
		var oldLen = _builder.Handle.BuildExtractValue(oldSpan, 1, "oldLen");
		var oldBytes = _builder.Handle.BuildMul(oldLen, elemSize, "oldBytes");
		var newBytes = _builder.Handle.BuildMul(newLen, elemSize, "newBytes");

		//reinterpret current storage as a byte span and realloc it
		var byteSpanType = LookupType(SpanType.Create(Types.Byte));
		var oldByteSpan = byteSpanType.Undef;
		oldByteSpan = _builder.Handle.BuildInsertValue(oldByteSpan, oldData, 0);
		oldByteSpan = _builder.Handle.BuildInsertValue(oldByteSpan, oldBytes, 1);
		var realloc = GetReallocFunc();
		var newByteSpan = _builder.Handle.BuildCall2(realloc.Type, realloc.Function, [oldByteSpan, newBytes]);
		var newData = _builder.Handle.BuildExtractValue(newByteSpan, 0, "newData");

		var newSpan = arrSpanType.Undef;
		newSpan = _builder.Handle.BuildInsertValue(newSpan, newData, 0);
		newSpan = _builder.Handle.BuildInsertValue(newSpan, newLen, 1);
		_values.Push(_builder.Handle.BuildStore(newSpan, arrPtr));
	}

	private void VisitMagicPropertyCall(MagicProperty mp, LLVMValueRef parent)
	{
		switch (mp.FullName) {
			case "span$Length":
				_values.Push(_builder.Handle.BuildExtractValue(parent, 1, "spanLength"));
				break;
			case "arr$Length":
				_values.Push(_builder.Handle.BuildExtractValue(parent, 1, "arrLength"));
				break;
			default:
				throw new NotImplementedException();
		}
	}

	private void VisitFieldDecl(FieldDecl fd, LLVMValueRef parent)
	{
		if (parent.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind) {
			var gep = _builder.Handle.BuildStructGEP2(LookupType(((IMemberSemantic)fd).ParentType), parent, (uint)fd.Index, $"{parent.Name}.{fd.Name}_gep");
			_values.Push(_builder.Handle.BuildLoad2(LookupType(fd.Type), gep));
		} else {
			_values.Push(_builder.Handle.BuildExtractValue(parent, (uint)fd.Index, $"{parent.Name}.{fd.Name}"));
		}
	}

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
		if (node.Expr.Semantic!.Type is InlineArrayType ia) {
			VisitInlineArrayIndexing(node, ia);
			return;
		}
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

	private void VisitInlineArrayIndexing(IndexingExpression node, InlineArrayType ia)
	{
		var elType = LookupType(ia.BaseType);
		var sem = node.Expr.Semantic;
		if (sem is VariableDecl vd) {
			var expr = _locals[vd.Name];
			VisitIndexing(expr, elType, node.Indices[0]);
		} else if (sem is FieldDecl fd) {
			Debug.Assert(node.Expr.Type == NodeType.MemberIdentifier);
			VisitFieldFlatIndexing((MemberIdentifier)node.Expr, fd, elType, node.Indices[0]);
		} else { 
			throw new NotImplementedException();
		}
	}

	private void VisitFieldFlatIndexing(MemberIdentifier expr, FieldDecl fd, LLVMTypeRef elType, Expression idxExpr)
	{
		var sem = expr.ParentExpr.Semantic;
		LLVMValueRef parent;
		switch (sem) {
			case GlobalFieldDecl gf:
				parent = _globals[gf.FullName];
				break;
			default:
				throw new NotImplementedException();
		}
		var count = _values.Count;
		Visit(idxExpr);
		Debug.Assert(_values.Count == count + 1);
		var idx = _values.Pop();
		var parentType = LookupType(expr.ParentExpr.Semantic!.Type);
		LLVMValueRef zero = LLVM.ConstInt(_module.Context.Int32Type, 0, 0);
		LLVMValueRef fieldIdx = LLVM.ConstInt(_module.Context.Int32Type, (ulong)fd.Index, 0);
		var result = _builder.Handle.BuildGEP2(parentType, parent, [zero, fieldIdx, idx], "index".AsSpan());
		result = _builder.Handle.BuildLoad2(elType, result, "element");
		_values.Push(result);
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
		var newData = _builder.Handle.BuildGEP2(elType, data, [start], "slice.ptr".AsSpan());
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
		if (targetType is NilableType nt) {
			return BuildCast(type, nt.BaseType, expr, typ);
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
		if (type == Types.Nil && targetType is TypeSystem.PointerType) {
			return expr;
		}
		if (type is TypeSystem.PointerType && targetType is RefType) {
			return expr;
		}
		if (type is SpanType) {
			var span = _builder.Handle.BuildExtractValue(expr, 0, "spanData");
			// the cast reinterprets the span's memory as the target.  If the target is already a ref
			// (eg. cast span: foo ref), the pointer type is that ref.  Otherwise wrap the type in a ref.
			var refType = targetType is RefType ? targetType : RefType.Create(targetType);
			return _builder.Handle.BuildPointerCast(
				_builder.Handle.BuildGEP2(_context.Handle.Int8Type, span, [LLVM.ConstInt(_context.Handle.Int32Type, 0, 0)], []),
				LookupType(refType));
		}
		throw new NotImplementedException();
	}

	public override void VisitRefExpression(RefExpression node)
	{
		var sem = node.Expr.Semantic;
		switch (sem) {
			case VariableDecl:
			case ParamDef:
				_values.Push(_locals[sem.Name]);
				break;
			default:
				throw new NotImplementedException();
		}
	}

	public override void VisitNewArrayExpression(NewArrayExpression node)
	{
		var elemType = LookupType(node.ArrayType);
		var elemSize = LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, SizeOf(elemType));
		var count = _values.Count;
		Visit(node.Size);
		Debug.Assert(_values.Count == count + 1);
		var arrayLen = _values.Pop();
		var byteLen = _builder.Handle.BuildMul(arrayLen, elemSize, "byteLen");
		var alloc = GetAllocFunc();
		var block = _builder.Handle.BuildCall2(alloc.Type, alloc.Function, [byteLen]);
		var data = _builder.Handle.BuildExtractValue(block, 0, "allocData");
		var spanType = LookupType(node.Semantic!.Type);
		var span = spanType.Undef;
		span = _builder.Handle.BuildInsertValue(span, data, 0);
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
		var alloc = GetAllocFunc();
		var block = _builder.Handle.BuildCall2(alloc.Type, alloc.Function, [typeLen]);
		var strPtr = _builder.Handle.BuildExtractValue(block, 0, "strData");
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
		_builder.Handle.BuildCall2(memcpy.Type, memcpy.Function, [
				dataPtrPtr, // dest
				data,        // src
				len//len64,       // size
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

	public override void VisitNilLiteralExpression(NilLiteralExpression node)
		=> _values.Push(LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(_context.Handle.VoidType, 0)));

	public override void VisitSelfLiteralExpression(SelfLiteralExpression node) => _values.Push(_isCtor ? _locals["self"] : _currentFunc.FirstParam);
}
