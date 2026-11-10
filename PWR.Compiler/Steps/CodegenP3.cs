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

public unsafe partial class CodegenP3(
		LLVMContext context, LLVMModuleRef module, string filename, bool isLibrary, bool debugInfo = false
	) : VisitorCompileStep
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
	// The types being generated, outermost first: what a member's symbol is built from, after the
	// namespace of the file they're in.
	private readonly Stack<string> _namespaces = [];

	private string? _fileNamespace;

	private string _runtimeNamespace = "pwr";

	private string Qualified(string member)
		=> _namespaces.Count == 0 ? member : $"{_fileNamespace}$${string.Join('$', _namespaces.Reverse())}${member}";
	private LLVMValueRef _last;
	private readonly Dictionary<string, (LLVMTypeRef Type, LLVMValueRef Function)> _functions = [];
	private readonly Dictionary<string, LLVMValueRef> _locals = [];
	private readonly Dictionary<string, LLVMValueRef> _globals = [];
	private readonly Dictionary<string, LLVMTypeRef> _builtinTypes = [];
	private readonly Dictionary<string, LLVMTypeRef> _customTypes = [];
	private readonly Dictionary<LLVMTypeRef, LLVMTypeRef> _spanTypes = [];
	private readonly List<(FunctionDeclaration, LLVMValueRef)> _functionDeclarations = [];
	private readonly List<LLVMValueRef> _moduleInits = [];
	private LLVMTypeRef _mainType;
	private LLVMValueRef _currentFunc;
	private bool _isCtor;
	private readonly Dictionary<InternalClass, FieldDeclaration[]> _fieldDefaults = [];
	private InternalClass? _currentCtorClass;

	public override Project Run(Project tree)
	{
		_builder = new(_context);
		_lValueVisitor = new(_module, _builder.Handle, _values, _locals, _globals, LookupType, LookupCompositeType, () => _currentFunc, () => _isCtor);

		_runtimeNamespace = tree.Imports.Any(i => i.Name == "pwr") ? "pwr" : _filename;
		LoadStdlib();
		if (_debugInfo) {
			InitDebugInfo();
		}
		LoadImports(tree);
		BuildObjectMetaclass();

		var result = base.Run(tree);
		foreach (var (node, value) in _functionDeclarations) {
			GenerateFunction(node, value);
		}
		WriteMetadata(tree);
		if (_isLibrary) {
			ExportLibraryId(tree.LibraryId);
		}
		if (_isLibrary || _deferredMetaclasses.Count > 0) {
			BuildMetaclassPatches();
		}
		if (!_isLibrary) {
			BuildProgramInit(tree.Imports, tree.EntryPoint);
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
	private void BeginDebugFunction(LLVMValueRef func, string name, Position pos, IType? returnType = null, IEnumerable<IType?>? parameterTypes = null)
	{
		if (!_debugInfo) {
			return;
		}
		var file = GetDIFile(pos.File);
		var line = (uint)Math.Max(pos.Line, 1);
		// The return type comes first (none for a function that returns nothing), then the parameters'.
		LLVMMetadataRef[] signature = [GetDIType(returnType), .. (parameterTypes ?? []).Select(GetDIType)];
		var subType = _diBuilder.CreateSubroutineType(file, signature, LLVMDIFlags.LLVMDIFlagZero);
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
		var line = (uint)Math.Max(pos.Line, 1);
		using var nameM = new MarshaledString(name);
		var varInfo = paramindex > 0
			? LLVM.DIBuilderCreateParameterVariable(_diBuilder, _diSubprogram, nameM, (nuint)nameM.Length, (uint)paramindex, file, line, diType, 1, LLVMDIFlags.LLVMDIFlagZero)
			: LLVM.DIBuilderCreateAutoVariable(_diBuilder, _diSubprogram, nameM, (nuint)nameM.Length, file, line, diType, 1, LLVMDIFlags.LLVMDIFlagZero, 0);
		var expr = LLVM.DIBuilderCreateExpression(_diBuilder, null, 0);
		var loc = _context.Handle.CreateDebugLocation(line, (uint)(pos.Column + 1), _diSubprogram, default);
		LLVM.DIBuilderInsertDeclareRecordAtEnd(_diBuilder, storage, varInfo, expr, loc, _builder.Handle.InsertBlock);
	}

	private void DescribeGlobal(LLVMValueRef global, GlobalFieldDecl field, Position pos)
	{
		if (!_debugInfo) {
			return;
		}
		var diType = GetDIType(field.Type);
		if (diType.Handle == default) {
			return;
		}
		var file = GetDIFile(pos.File);
		var line = (uint)Math.Max(pos.Line, 1);
		using var displayName = new MarshaledString($"{field.Md.Name}::{field.Name}");
		using var linkageName = new MarshaledString(global.Name);
		var expression = LLVM.DIBuilderCreateExpression(_diBuilder, null, 0);
		var description = LLVM.DIBuilderCreateGlobalVariableExpression(_diBuilder, _diCompileUnit, displayName, (nuint)displayName.Length,
			linkageName, (nuint)linkageName.Length, file, line, diType, 0, expression, default, 0);
		LLVM.GlobalSetMetadata(global, 0, description);
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
				MetaclassType => PointerDIType(default, type.Name),
				StringType => PointerDIType(StringStructDIType(), "string"),
				SpanType st => SpanDIType(type, st.ElementType),
				TypeSystem.ArrayType at => SpanDIType(type, at.ElementType),
				InternalStruct ist => StructDIType(type, ist),
				InlineArrayType ia => InlineArrayDIType(ia),
				IClassType cls => PointerDIType(ClassDIType(cls), cls.Name),
				// Anything else has no description yet, and so isn't shown, rather than failing the build.
				_ => default,
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
			members[1] = LLVM.DIBuilderCreateMemberType(_diBuilder, default, bytes, (nuint)bytes.Length,
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
		using (var ln = new MarshaledString("length")) {
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

	private readonly Dictionary<IClassType, LLVMMetadataRef> _diObjects = [];

	private LLVMMetadataRef ClassDIType(IClassType cls)
	{
		if (_diObjects.TryGetValue(cls, out var cached)) {
			return cached;
		}
		var llvmType = GetClassStructType(cls);
		var pos = cls is InternalClass ic ? ic.Decl.Position : default;
		var file = GetDIFile(pos.File);
		var line = (uint)Math.Max(pos.Line, 1);
		using var name = new MarshaledString(cls.Name);
		using var uniqueId = new MarshaledString(cls.FullName);
		var fwd = LLVM.DIBuilderCreateReplaceableCompositeType(_diBuilder, DW_TAG_STRUCTURE_TYPE,
			name, (nuint)name.Length, _diCompileUnit, file, line, 0, SizeInBits(llvmType), 0,
			LLVMDIFlags.LLVMDIFlagZero, uniqueId, (nuint)uniqueId.Length);
		_diObjects[cls] = fwd;

		var members = new List<LLVMMetadataRef>();
		if (cls is ObjectType) {
			using var metaclass = new MarshaledString("metaclass");
			members.Add(LLVM.DIBuilderCreateMemberType(_diBuilder, fwd, metaclass, (nuint)metaclass.Length, file, line,
				POINTER_BIT_SIZE, 0, 0, LLVMDIFlags.LLVMDIFlagZero, PointerDIType(default, "metaclass")));
		} else {
			members.Add(LLVM.DIBuilderCreateInheritance(_diBuilder, fwd, ClassDIType(cls.BaseType!), 0, 0, LLVMDIFlags.LLVMDIFlagZero));
			var fields = cls.Fields;
			for (var i = 0; i < fields.Length; ++i) {
				using var fieldName = new MarshaledString(fields[i].Name);
				members.Add(LLVM.DIBuilderCreateMemberType(_diBuilder, fwd, fieldName, (nuint)fieldName.Length, file, line,
					SizeInBits(LookupType(fields[i].Type)), 0, OffsetInBits(llvmType, (uint)i + 1), LLVMDIFlags.LLVMDIFlagZero, GetDIType(fields[i].Type)));
			}
		}
		var array = members.ToArray();
		LLVMMetadataRef result;
		fixed (LLVMMetadataRef* first = array) {
			result = LLVM.DIBuilderCreateStructType(_diBuilder, _diCompileUnit, name, (nuint)name.Length, file, line,
				SizeInBits(llvmType), 0, LLVMDIFlags.LLVMDIFlagZero, default, (LLVMOpaqueMetadata**)first, (uint)array.Length, 0, default,
				uniqueId, (nuint)uniqueId.Length);
		}
		LLVM.MetadataReplaceAllUsesWith(fwd, result);
		_diObjects[cls] = result;
		return result;
	}

	private LLVMMetadataRef InlineArrayDIType(InlineArrayType ia)
	{
		var llvmType = LookupType(ia);
		var count = (long)(SizeOf(llvmType) / SizeOf(LookupType(ia.ElementType)));
		var subs = stackalloc LLVMMetadataRef[1];
		subs[0] = LLVM.DIBuilderGetOrCreateSubrange(_diBuilder, 0, count);
		return LLVM.DIBuilderCreateArrayType(_diBuilder, SizeInBits(llvmType), 0,
			GetDIType(ia.ElementType), (LLVMOpaqueMetadata**)subs, 1);
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
		var name = _runtimeNamespace + "$$Memory$Alloc";
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
		var name = _runtimeNamespace + "$$Memory$Realloc";
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

	private LLVMTypeRef? _objectBaseType;

	private LLVMTypeRef GetObjectBaseType()
	{
		if (_objectBaseType is not { } t) {
			t = _context.Handle.CreateNamedStruct("Object");
			t.StructSetBody([_builtinTypes["ptr"]], false);
			_customTypes.Add("Object", t);
			_objectBaseType = t;
		}
		return t;
	}

	private LLVMTypeRef? _metaclassHeaderType;

	private LLVMTypeRef GetMetaclassHeaderType()
	{
		if (_metaclassHeaderType is not { } t) {
			t = _context.Handle.CreateNamedStruct("MetaclassHeader");
			var ptr = _builtinTypes["ptr"];
			t.StructSetBody([ptr, _context.Handle.Int32Type, ptr, _context.Handle.Int32Type], false);
			_metaclassHeaderType = t;
		}
		return t;
	}

	private LLVMTypeRef? _metaclassAccessType;

	private LLVMTypeRef GetMetaclassAccessType()
	{
		if (_metaclassAccessType is not { } t) {
			t = _context.Handle.GetStructType([GetMetaclassHeaderType(), LLVMTypeRef.CreateArray2(_builtinTypes["ptr"], 0)], false);
			_metaclassAccessType = t;
		}
		return t;
	}

	private LLVMValueRef BuildVmtLoad(LLVMValueRef self, int slot)
		=> BuildVmtLoadFromMetaclass(_builder.Handle.BuildLoad2(_builtinTypes["ptr"], self, "metaclass"), slot);

	private LLVMValueRef BuildVmtLoadFromMetaclass(LLVMValueRef metaclass, int slot)
		=> _builder.Handle.BuildLoad2(_builtinTypes["ptr"], BuildVmtSlotAddress(metaclass, slot), "vmtEntry");

	private LLVMValueRef BuildVmtSlotAddress(LLVMValueRef metaclass, int slot)
	{
		var i32 = _context.Handle.Int32Type;
		LLVMValueRef[] indices = [
			LLVMValueRef.CreateConstInt(i32, 0),
			LLVMValueRef.CreateConstInt(i32, 1), // the VMT array, after the header
			LLVMValueRef.CreateConstInt(i32, (ulong)slot),
		];
		return _builder.Handle.BuildInBoundsGEP2(GetMetaclassAccessType(), metaclass, indices, "vmtSlot".AsSpan());
	}

	private static bool IsExternalClassConstructor(ISemantic? semantic)
		=> semantic is ExternalMethod { Parent: IClassType } method && method.SemanticType.HasFlag(SemanticType.Constructor);

	private static bool IsClassScope(object? semantic)
		=> semantic is IFunction { Flags: var flags } && flags.HasFlag(FunctionFlags.ClassScope);

	private LLVMValueRef BuildMetaclassOperand(Expression receiver)
	{
		var ptr = _builtinTypes["ptr"];
		var semantic = receiver.Semantic!;
		if (receiver is not SelfLiteralExpression && semantic.SemanticType.HasFlag(SemanticType.Type)) {
			return _builder.Handle.BuildBitCast(GetOrCreateMetaclass((IClassType)semantic.Type), ptr);
		}
		Visit(receiver);
		var value = _values.Pop();
		return semantic.Type is MetaclassType ? value : _builder.Handle.BuildLoad2(ptr, value, "metaclass");
	}

	private LLVMTypeRef GetVirtualMethodType(IMethod method)
	{
		if (_functions.TryGetValue(method.FullName, out var generated)) {
			return generated.Type;
		}
		var args = method.Args.Where(a => a.Name.Name != "self").Select(a => LookupType(a.ParamType));
		var semantic = (ISemantic)method;
		var returnType = semantic.SemanticType.HasFlag(SemanticType.Constructor) ? _context.Handle.VoidType : LookupType(semantic.Type);
		return LLVMTypeRef.CreateFunction(returnType, [_builtinTypes["ptr"], .. args], false);
	}

	private LLVMValueRef BuildNameConstant(string value)
	{
		var strBytes = Encoding.UTF8.GetBytes(value);
		var arrayType = LLVMTypeRef.CreateArray(_context.Handle.Int8Type, (uint)strBytes.Length + 1);
		var pchar = _context.Handle.GetConstString(value, false);
		var global = _module.AddGlobal(arrayType, "$className$");
		global.Initializer = pchar;
		global.Linkage = LLVMLinkage.LLVMPrivateLinkage;
		global.IsGlobalConstant = true;
		return global;
	}

	private LLVMValueRef _objectMetaclass;
	private bool _hasObjectMetaclass;

	private void BuildObjectMetaclass()
	{
		if (!_hasObjectMetaclass) {
			var global = BuildMetaclassGlobal(
				"Object$Metaclass", LLVM.ConstNull(_builtinTypes["ptr"]), SizeOf(GetObjectBaseType()), "Object", []);
			_objectMetaclass = global;
			_hasObjectMetaclass = true;
			_metaclasses.Add("Object$Metaclass", global);
		}
	}

	private LLVMTypeRef GetMetaclassOuterType(int slots)
		=> _context.Handle.GetStructType([GetMetaclassHeaderType(), LLVMTypeRef.CreateArray2(_builtinTypes["ptr"], (ulong)slots)], false);

	private LLVMValueRef DeclareMetaclassGlobal(string name, int slots, string? exportedAs = null, bool isConstant = true)
	{
		var global = _module.AddGlobal(GetMetaclassOuterType(slots), exportedAs ?? name);
		if (exportedAs != null) {
			global.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLExportStorageClass;
		} else {
			global.Linkage = LLVMLinkage.LLVMPrivateLinkage;
		}
		global.IsGlobalConstant = isConstant;
		return global;
	}

	private static string MetaclassSymbol(string classFullName) => $"{classFullName}$Metaclass";

	private void InitializeMetaclassGlobal(LLVMValueRef global, LLVMValueRef parent, ulong instanceSize, string className, LLVMValueRef[] virtuals)
	{
		var i32 = _context.Handle.Int32Type;
		var ptr = _builtinTypes["ptr"];
		var headerType = GetMetaclassHeaderType();
		var header = LLVMValueRef.CreateConstNamedStruct(headerType, [
			parent,
			LLVMValueRef.CreateConstInt(i32, instanceSize),
			BuildNameConstant(className),
			LLVMValueRef.CreateConstInt(i32, (ulong)className.Length),
		]);
		global.Initializer = LLVMValueRef.CreateConstNamedStruct(GetMetaclassOuterType(virtuals.Length), [header, LLVMValueRef.CreateConstArray(ptr, virtuals)]);
	}

	private LLVMValueRef BuildMetaclassGlobal(string name, LLVMValueRef parent, ulong instanceSize, string className, LLVMValueRef[] virtuals)
	{
		var global = DeclareMetaclassGlobal(name, virtuals.Length);
		InitializeMetaclassGlobal(global, parent, instanceSize, className, virtuals);
		return global;
	}

	private readonly Dictionary<string, LLVMValueRef> _metaclasses = [];

	private LLVMValueRef GetOrCreateMetaclass(IClassType cls)
	{
		if (cls is ExternalClass external) {
			var symbol = MetaclassSymbol(external.FullName);
			if (!_metaclasses.TryGetValue(symbol, out var imported)) {
				imported = _module.AddGlobal(GetMetaclassOuterType(cls.Vmt.Length), symbol);
				imported.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLImportStorageClass;
				imported.IsGlobalConstant = true;
				_metaclasses.Add(symbol, imported);
			}
			return imported;
		}
		var name = MetaclassSymbol(cls.FullName);
		if (_metaclasses.TryGetValue(name, out var global)) {
			return global;
		}
		var internalClass = (InternalClass)cls;
		var deferred = IsDeferred(internalClass);
		global = DeclareMetaclassGlobal(name, cls.Vmt.Length, _isLibrary ? name : null, isConstant: !deferred);
		_metaclasses.Add(name, global);
		var empty = LLVMValueRef.CreateConstNull(_builtinTypes["ptr"]);
		var virtuals = cls.Vmt.Select((entry, slot) => HowSlotIsFilled(internalClass, slot) == SlotFill.Static ? BuildVmtSlotValue(entry) : empty).ToArray();
		var parent = cls.BaseType is ExternalClass ? empty : GetOrCreateMetaclass(cls.BaseType!);
		InitializeMetaclassGlobal(global, parent, SizeOf(GetClassStructType(cls)), cls.Name, virtuals);
		if (deferred) {
			_deferredMetaclasses.Add((internalClass, global));
		}
		return global;
	}

	private bool IsDeferred(InternalClass cls)
	{
		if (!_deferredClasses.TryGetValue(cls, out var result)) {
			result = cls.BaseType is ExternalClass
				|| (cls.BaseType is InternalClass baseClass && IsDeferred(baseClass))
				|| cls.Vmt.Any(ReferencesExternalMetaclass);
			_deferredClasses.Add(cls, result);
		}
		return result;
	}

	private readonly Dictionary<InternalClass, bool> _deferredClasses = [];
	private readonly List<(InternalClass Class, LLVMValueRef Global)> _deferredMetaclasses = [];

	private static bool ReferencesExternalMetaclass(VmtEntry entry)
		=> entry.IsConstant && entry.Value is MetaclassExpression { Type.Semantic.Type: ExternalClass };

	private enum SlotFill
	{
		Static,        // written into the program's data
		CopyFromBase,  // inherited from a base class that isn't complete until load: copied from its metaclass
		Store,         // the metaclass of a class in another library: stored when the program starts
	}

	private SlotFill HowSlotIsFilled(InternalClass cls, int slot)
	{
		var entry = cls.Vmt[slot];
		var baseVmt = cls.BaseType.Vmt;
		var inherited = slot < baseVmt.Length && baseVmt[slot].FullName == entry.FullName;
		if (inherited && (cls.BaseType is ExternalClass || (cls.BaseType is InternalClass baseClass && IsDeferred(baseClass)))) {
			return SlotFill.CopyFromBase;
		}
		return ReferencesExternalMetaclass(entry) ? SlotFill.Store : SlotFill.Static;
	}

	private LLVMValueRef? _patchFunc;

	private LLVMValueRef GetPatchFunc()
	{
		if (_patchFunc is not { } func) {
			func = _module.AddFunction("$PatchMetaclasses$", _mainType);
			func.Linkage = LLVMLinkage.LLVMInternalLinkage;
			_patchFunc = func;
		}
		return func;
	}

	private void BuildMetaclassPatches()
	{
		var func = GetPatchFunc();
		_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
		ClearDebugScope();
		var ptr = _builtinTypes["ptr"];
		for (var n = 0; n < _deferredMetaclasses.Count; ++n) {
			var (cls, global) = _deferredMetaclasses[n];
			var metaclass = _builder.Handle.BuildBitCast(global, ptr);
			if (cls.BaseType is ExternalClass external) {
				var parentField = _builder.Handle.BuildStructGEP2(GetMetaclassHeaderType(), metaclass, 0, "parentField");
				_builder.Handle.BuildStore(_builder.Handle.BuildBitCast(GetOrCreateMetaclass(external), ptr), parentField);
			}
			var baseMetaclass = _builder.Handle.BuildBitCast(GetOrCreateMetaclass(cls.BaseType!), ptr);
			for (var slot = 0; slot < cls.Vmt.Length; ++slot) {
				var fill = HowSlotIsFilled(cls, slot);
				if (fill == SlotFill.Static) {
					continue;
				}
				var value = fill == SlotFill.CopyFromBase
					? _builder.Handle.BuildLoad2(ptr, BuildVmtSlotAddress(baseMetaclass, slot), "inherited")
					: BuildVmtSlotValue(cls.Vmt[slot]);
				_builder.Handle.BuildStore(value, BuildVmtSlotAddress(metaclass, slot));
			}
		}
		_builder.Handle.BuildRetVoid();
	}

	private LLVMValueRef BuildVmtSlotValue(VmtEntry entry)
	{
		var ptr = _builtinTypes["ptr"];
		if (entry.IsAbstract) {
			return LLVM.ConstNull(ptr);
		}
		if (!entry.IsConstant) {
			return _functions[entry.FullName].Function;
		}
		var value = BuildConstantValue(entry);
		return value.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind ? value : LLVM.ConstIntToPtr(value, ptr);
	}

	private LLVMValueRef BuildConstantValue(VmtEntry entry)
	{
		var type = LookupType(entry.ConstantType!);
		if (type.Kind == LLVMTypeKind.LLVMIntegerTypeKind && IntegerValueOf(entry.Value!) is { } raw) {
			return LLVMValueRef.CreateConstInt(type, raw);
		}
		Visit(entry.Value);
		return _values.Pop();
	}

	private static ulong? IntegerValueOf(Expression literal) => literal switch {
		IntegerLiteralExpression il => (ulong)(long)il.Value,
		CharLiteralExpression cl => cl.Value,
		BooleanLiteralExpression bl => bl.Value ? 1UL : 0UL,
		_ => null,
	};

	private void VisitClassConstantRead(MemberIdentifier node, IClassConstant constant)
	{
		var receiver = node.ParentExpr;
		if (receiver is not SelfLiteralExpression && receiver.Semantic is { SemanticType: var kind, Type: IClassType cls } && kind.HasFlag(SemanticType.Type)
			&& !ReferencesExternalMetaclass(cls.Vmt[cls.SlotOf(constant.Name)])) {
			_values.Push(BuildConstantValue(cls.Vmt[cls.SlotOf(constant.Name)]));
			return;
		}
		var slot = ((IClassType)((IMemberSemantic)constant).ParentType).SlotOf(constant.Name);
		var address = BuildVmtSlotAddress(BuildMetaclassOperand(receiver), slot);
		_values.Push(_builder.Handle.BuildLoad2(LookupType(constant.Type), address, constant.Name));
	}

	private (LLVMTypeRef Type, LLVMValueRef Function) GetNewObjectFunc()
	{
		const string name = "$NewObject$";
		if (!_functions.TryGetValue(name, out var result)) {
			var ptrType = _builtinTypes["ptr"];
			var type = LLVMTypeRef.CreateFunction(ptrType, [ptrType], false);
			var func = _module.AddFunction(name, type);
			result = (type, func);
			_functions.Add(name, result);

			var preserved = _builder.Handle.InsertBlock;
			var preservedLoc = _debugInfo ? LLVM.GetCurrentDebugLocation2(_builder.Handle) : default;
			if (_debugInfo) {
				LLVM.SetCurrentDebugLocation2(_builder.Handle, default);
			}
			try {
				_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
				var metaclass = func.GetParam(0);
				var sizePtr = _builder.Handle.BuildStructGEP2(GetMetaclassHeaderType(), metaclass, 1, "sizePtr");
				var size = _builder.Handle.BuildLoad2(_context.Handle.Int32Type, sizePtr, "size");
				var alloc = GetAllocFunc();
				var block = _builder.Handle.BuildCall2(alloc.Type, alloc.Function, [size]);
				var data = _builder.Handle.BuildExtractValue(block, 0, "data");
				var mcSlot = _builder.Handle.BuildStructGEP2(GetObjectBaseType(), data, 0, "mcSlot");
				_builder.Handle.BuildStore(metaclass, mcSlot);
				_builder.Handle.BuildRet(data);
			} finally {
				_builder.Handle.PositionAtEnd(preserved);
				if (_debugInfo) {
					LLVM.SetCurrentDebugLocation2(_builder.Handle, preservedLoc);
				}
			}
		}
		return result;
	}

	// Object's own constructor: no source, does nothing, exists only as the base case that
	// every class's `super Create()` chain must eventually reach.
	private (LLVMTypeRef Type, LLVMValueRef Function) GetObjectCreateFunc()
	{
		const string name = "Object$Create";
		if (!_functions.TryGetValue(name, out var result)) {
			var ptrType = _builtinTypes["ptr"];
			var type = LLVMTypeRef.CreateFunction(_context.Handle.VoidType, [ptrType], false);
			var func = _module.AddFunction(name, type);
			result = (type, func);
			_functions.Add(name, result);

			var preserved = _builder.Handle.InsertBlock;
			var preservedLoc = _debugInfo ? LLVM.GetCurrentDebugLocation2(_builder.Handle) : default;
			if (_debugInfo) {
				LLVM.SetCurrentDebugLocation2(_builder.Handle, default);
			}
			try {
				_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
				_builder.Handle.BuildRetVoid();
			} finally {
				_builder.Handle.PositionAtEnd(preserved);
				if (_debugInfo) {
					LLVM.SetCurrentDebugLocation2(_builder.Handle, preservedLoc);
				}
			}
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
		var hdrBytes = MemoryMarshal.AsBytes(new Span<MetadataHeader>(ref header));
		byte[] bytes = [.. hdrBytes, .. tree.Metadata];

		var parts = new List<LLVMValueRef>();
		var pos = 0;
		foreach (var fixup in tree.MetaclassFixups) {
			var at = hdrBytes.Length + fixup.Offset;
			AddBytes(parts, bytes.AsSpan(pos, at - pos));
			parts.Add(BuildRva(GetOrCreateMetaclass(fixup.Class)));
			pos = at + sizeof(uint);
		}
		AddBytes(parts, bytes.AsSpan(pos));
		var metaType = _context.Handle.GetStructType([.. parts.Select(p => p.TypeOf)], true);
		var global = _module.AddGlobal(metaType, "$metadata$");
		global.Initializer = _context.Handle.GetConstStruct([.. parts], true);
		global.IsGlobalConstant = true;
		global.Section = ".PWRMeta";

		bytes = tree.BlobData;
		global = _module.AddGlobal(LLVMTypeRef.CreateArray(_context.Handle.Int8Type, (uint)bytes.Length), "$metaBlob$");
		global.Initializer = BuildBytes(bytes);
		global.IsGlobalConstant = true;
		global.Section = ".PWRBlob";
	}

	private void AddBytes(List<LLVMValueRef> parts, ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length > 0) {
			parts.Add(BuildBytes(bytes));
		}
	}

	private LLVMValueRef BuildBytes(ReadOnlySpan<byte> bytes)
	{
		fixed (byte* ptr = bytes) {
			return LLVM.ConstStringInContext(_context.Handle, (sbyte*)ptr, (uint)bytes.Length, 1);
		}
	}

	// i32 RVA of a global within the image being linked: trunc(global - __ImageBase). For COFF,
	// LLVM recognizes exactly this shape and lowers it to an image-relative relocation
	// (IMAGE_REL_AMD64_ADDR32NB) that the linker resolves.
	private LLVMValueRef BuildRva(LLVMValueRef global)
	{
		var i32 = _context.Handle.Int32Type;
		var i64 = _context.Handle.Int64Type;
		var imageBase = _module.GetNamedGlobal("__ImageBase");
		if (imageBase.Handle == 0) {
			imageBase = _module.AddGlobal(_context.Handle.Int8Type, "__ImageBase");
		}
		var diff = LLVMValueRef.CreateConstSub(LLVMValueRef.CreateConstPtrToInt(global, i64), LLVMValueRef.CreateConstPtrToInt(imageBase, i64));
		return LLVMValueRef.CreateConstTrunc(diff, i32);
	}

	// What a program exits with when a library it imports isn't the version it was built against.
	private const int LIBRARY_MISMATCH_EXIT_CODE = 0x50570001;

	private static string IdSymbol(string library) => $"{library}$id$";

	private void ExportLibraryId(Guid id)
	{
		var global = _module.AddGlobal(LLVMTypeRef.CreateArray(_context.Handle.Int8Type, 16), IdSymbol(_filename));
		global.Initializer = BuildBytes(id.ToByteArray());
		global.IsGlobalConstant = true;
		global.Alignment = 8;
		global.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLExportStorageClass;
	}

	private LLVMValueRef GetImportedFunction(string name, LLVMTypeRef type)
	{
		var result = _module.GetNamedFunction(name);
		if (result.Handle == 0) {
			result = _module.AddFunction(name, type);
			result.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLImportStorageClass;
		}
		return result;
	}

	private (LLVMTypeRef Type, LLVMValueRef Function) GetLibraryMismatchFunc()
	{
		const string name = "$LibraryMismatch$";
		if (_functions.TryGetValue(name, out var result)) {
			return result;
		}
		var ptr = _builtinTypes["ptr"];
		var i32 = _context.Handle.Int32Type;
		var type = LLVMTypeRef.CreateFunction(_context.Handle.VoidType, [ptr, i32], false);
		var func = _module.AddFunction(name, type);
		func.Linkage = LLVMLinkage.LLVMInternalLinkage;
		result = (type, func);
		_functions.Add(name, result);

		var preserved = _builder.Handle.InsertBlock;
		var preservedLoc = _debugInfo ? LLVM.GetCurrentDebugLocation2(_builder.Handle) : default;
		if (_debugInfo) {
			LLVM.SetCurrentDebugLocation2(_builder.Handle, default);
		}
		try {
			_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
			var getStdHandleType = LLVMTypeRef.CreateFunction(ptr, [i32], false);
			var writeFileType = LLVMTypeRef.CreateFunction(i32, [ptr, ptr, i32, ptr, ptr], false);
			var exitType = LLVMTypeRef.CreateFunction(_context.Handle.VoidType, [i32], false);
			var getStdHandle = GetImportedFunction("GetStdHandle", getStdHandleType);
			var writeFile = GetImportedFunction("WriteFile", writeFileType);
			var exitProcess = GetImportedFunction("ExitProcess", exitType);
			// STD_ERROR_HANDLE is -12.
			var stderr = _builder.Handle.BuildCall2(getStdHandleType, getStdHandle, [LLVMValueRef.CreateConstInt(i32, 0xFFFFFFF4)]);
			var written = _builder.Handle.BuildAlloca(i32, "written");
			_builder.Handle.BuildCall2(writeFileType, writeFile, [stderr, func.GetParam(0), func.GetParam(1), written, LLVMValueRef.CreateConstNull(ptr)]);
			_builder.Handle.BuildCall2(exitType, exitProcess, [LLVMValueRef.CreateConstInt(i32, LIBRARY_MISMATCH_EXIT_CODE)]);
			_builder.Handle.BuildUnreachable();
		} finally {
			_builder.Handle.PositionAtEnd(preserved);
			if (_debugInfo) {
				LLVM.SetCurrentDebugLocation2(_builder.Handle, preservedLoc);
			}
		}
		return result;
	}

	private void BuildImportChecks(LLVMValueRef func, IEnumerable<ExternalLibrary> imports)
	{
		var ptr = _builtinTypes["ptr"];
		var i8 = _context.Handle.Int8Type;
		var i32 = _context.Handle.Int32Type;
		var i64 = _context.Handle.Int64Type;
		var mismatch = GetLibraryMismatchFunc();
		foreach (var import in imports) {
			var symbol = IdSymbol(import.Name);
			var id = _module.GetNamedGlobal(symbol);
			if (id.Handle == 0) {
				id = _module.AddGlobal(LLVMTypeRef.CreateArray(i8, 16), symbol);
				id.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLImportStorageClass;
				id.IsGlobalConstant = true;
			}
			var expected = import.Id.ToByteArray();
			var low = _builder.Handle.BuildLoad2(i64, id, "idLow");
			low.Alignment = 1;
			LLVMValueRef[] highOffset = [LLVMValueRef.CreateConstInt(i32, 8)];
			var highAddress = _builder.Handle.BuildInBoundsGEP2(i8, id, highOffset, "idHighAddress");
			var high = _builder.Handle.BuildLoad2(i64, highAddress, "idHigh");
			high.Alignment = 1;
			var differs = _builder.Handle.BuildOr(
				_builder.Handle.BuildICmp(LLVMIntPredicate.LLVMIntNE, low, LLVMValueRef.CreateConstInt(i64, BitConverter.ToUInt64(expected, 0))),
				_builder.Handle.BuildICmp(LLVMIntPredicate.LLVMIntNE, high, LLVMValueRef.CreateConstInt(i64, BitConverter.ToUInt64(expected, 8))),
				"differs");
			var bad = func.AppendBasicBlock("wrongVersion");
			var good = func.AppendBasicBlock("rightVersion");
			_builder.Handle.BuildCondBr(differs, bad, good);
			_builder.Handle.PositionAtEnd(bad);
			var message = $"PWRFL: '{_filename}' was built against a different version of '{import.Name}'. Rebuild '{_filename}'.\r\n";
			_builder.Handle.BuildCall2(mismatch.Type, mismatch.Function, [
				_builder.Handle.BuildBitCast(BuildNameConstant(message), ptr),
				LLVMValueRef.CreateConstInt(i32, (ulong)Encoding.UTF8.GetByteCount(message)),
			]);
			_builder.Handle.BuildUnreachable();
			_builder.Handle.PositionAtEnd(good);
		}
	}

	private void BuildLibraryInitPrologue(LLVMValueRef func, IEnumerable<ExternalLibrary> imports)
	{
		var i8 = _context.Handle.Int8Type;
		var flag = _module.AddGlobal(i8, "$initialized$");
		flag.Linkage = LLVMLinkage.LLVMInternalLinkage;
		flag.Initializer = LLVMValueRef.CreateConstInt(i8, 0);
		var done = _builder.Handle.BuildICmp(LLVMIntPredicate.LLVMIntNE, _builder.Handle.BuildLoad2(i8, flag, "initialized"), LLVMValueRef.CreateConstInt(i8, 0));
		var already = func.AppendBasicBlock("alreadyInitialized");
		var first = func.AppendBasicBlock("firstTime");
		_builder.Handle.BuildCondBr(done, already, first);
		_builder.Handle.PositionAtEnd(already);
		_builder.Handle.BuildRetVoid();
		_builder.Handle.PositionAtEnd(first);
		_builder.Handle.BuildStore(LLVMValueRef.CreateConstInt(i8, 1), flag);
		var list = imports.ToList();
		BuildImportChecks(func, list);
		foreach (var import in list) {
			_builder.Handle.BuildCall2(_mainType, GetImportedFunction($"{import.Name}$init$", _mainType), []);
		}
		_builder.Handle.BuildCall2(_mainType, GetPatchFunc(), []);
	}

	private void BuildProgramInit(List<ExternalLibrary> imports, LLVMValueRef entryPoint)
	{
		var funcType = LLVMTypeRef.CreateFunction(_context.Handle.Int32Type, []);
		var func = _module.AddFunction("runtimeMain", funcType);
		_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
		ClearDebugScope();
		BuildImportChecks(func, imports);
		foreach (var im in imports) {
			_builder.Handle.BuildCall2(_mainType, GetImportedFunction($"{im.Name}$init$", _mainType), []);
		}
		if (_deferredMetaclasses.Count > 0) {
			_builder.Handle.BuildCall2(_mainType, GetPatchFunc(), []);
		}
		if (_programInit is { } programInit) {
			_builder.Handle.BuildCall2(_mainType, programInit, []);
		}
		if (entryPoint.Handle != default) {
			_builder.Handle.BuildCall2(_mainType, entryPoint, []);
		}
		var exitType = LLVMTypeRef.CreateFunction(_context.Handle.VoidType, [_context.Handle.Int32Type]);
		var exitProcess = GetImportedFunction("ExitProcess", exitType);
		_builder.Handle.BuildCall2(exitType, exitProcess, [LLVM.ConstInt(_context.Handle.Int32Type, 0, 0)]);
		_builder.Handle.BuildUnreachable();
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
		if (_moduleInits.Count > 0 || _isLibrary) {
			var func = _module.AddFunction(_filename + "$init$", _mainType);
			_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
			ClearDebugScope();
			if (_isLibrary) {
				BuildLibraryInitPrologue(func, node.Imports);
			}
			foreach (var mi in _moduleInits) {
				_builder.Handle.BuildCall2(_mainType, mi, []);
			}
			_builder.Handle.BuildRetVoid();
			if (_isLibrary) {
				func.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLExportStorageClass;
			} else {
				func.Linkage = LLVMLinkage.LLVMInternalLinkage;
				_programInit = func;
			}
		}
	}

	private LLVMValueRef? _programInit;

	public override void VisitCodeFile(CodeFile node)
	{
		_fileNamespace = node.Namespace;
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
			GetStructType((InternalStruct)node.Semantic!.Type);
			Visit(node.Body);
		}
		finally {  
			_namespaces.Pop();
		}
	}

	public override void VisitClassDeclaration(ClassDeclaration node)
	{
		_namespaces.Push(node.Name.ToString());
		try {
			var cls = (InternalClass)node.Semantic!.Type;
			_fieldDefaults[cls] = [.. node.Body.OfType<FieldDeclaration>().Where(d => d.Value != null && !d.Flags.HasFlag(FunctionFlags.ClassScope))];
			GetClassStructType(cls);
			Visit(node.Body);
		}
		finally {
			_namespaces.Pop();
		}
	}

	private LLVMTypeRef GetStructType(InternalStruct ist)
	{
		if (!_customTypes.TryGetValue(ist.Name, out var typ)) {
			typ = _context.Handle.CreateNamedStruct(ist.Decl.Name.Semantic!.FullName);
			_customTypes.Add(ist.Name, typ);
			typ.StructSetBody([.. ist.Fields.Select(f => LookupType(f.Type))], false);
		}
		return typ;
	}

	private LLVMTypeRef GetClassStructType(IClassType cls)
	{
		if (cls is ObjectType) {
			return GetObjectBaseType();
		}
		var (key, baseType, fields) = cls switch {
			InternalClass ic => (ic.FullName, ic.BaseType, ic.Fields),
			ExternalClass ec => (ec.FullName, ec.BaseType!, ec.Fields),
			_ => throw new NotImplementedException($"The layout of '{cls.Name}'"),
		};
		if (!_customTypes.TryGetValue(key, out var typ)) {
			typ = _context.Handle.CreateNamedStruct(key);
			_customTypes.Add(key, typ);
			typ.StructSetBody([GetClassStructType(baseType), .. fields.Select(f => LookupType(f.Type))], false);
		}
		return typ;
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
		var func = _module.AddFunction($"{_fileNamespace}$${node.Name}$$init", _mainType);
		_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
		ClearDebugScope();
		foreach (var init in inits) {
			Debug.Assert(_values.Count == 0);
			Visit(init.Value);
			Debug.Assert(_values.Count == 1);
			_builder.Handle.BuildStore(_values.Pop(), _globals[Qualified(init.Decl.Name)]);
		}
		_builder.Handle.BuildRetVoid();
		_moduleInits.Add(func);
		func.Linkage = LLVMLinkage.LLVMInternalLinkage;
	}

	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		// An abstract member of a class has no implementation.
		if (node.Flags.HasFlag(FunctionFlags.Abstract) && node.Semantic is MethodDef { Owner.Type: IClassType }) {
			return;
		}
		var funcType = BuildFuncType(node);
		if (node.Flags.HasFlag(FunctionFlags.Abstract)) {
			BuildAbstractFunction(node, funcType);
			return;
		}
		var name = Qualified(node.Name.Name);
		LLVMValueRef func;
		if (_forwardDeclared.Remove(name)) {
			func = _functions[name].Function;
		} else {
			func = _module.AddFunction(name, funcType);
			_functions.Add(name, (funcType, func));
		}
		_functionDeclarations.Add((node, func));
	}

	private void GenerateFunction(FunctionDeclaration node, LLVMValueRef func)
	{
		_currentFunc = func;
		_locals.Clear();

		_builder.Handle.PositionAtEnd(func.AppendBasicBlock("entry"));
		var displayName = node.Semantic is FunctionDef { Owner: { } owner } ? $"{owner.Name}::{node.Name.Name}" : node.Name.Name;
		BeginDebugFunction(func, displayName, node.Position, node.ReturnType?.Semantic?.Type, node.Parameters.Select(p => p.Semantic?.Type));
		for (int i = 0; i < node.Parameters.Length; ++i) {
			var param = func.GetParam((uint)i);
			param.Name = node.Parameters[i].Name.Name;
			var alloc = _builder.Handle.BuildAlloca(param.TypeOf, $"param${param.Name}");
			_locals[param.Name] = alloc;
			_builder.Handle.BuildStore(param, alloc);
			DeclareLocal(alloc, param.Name, node.Parameters[i].Semantic!.Type, node.Parameters[i].Position, i + 1);
		}

		var isStructCtor = node.IsConstructor && node.Semantic is Constructor { Owner.Type: InternalStruct };
		if (isStructCtor) {
			_isCtor = true;
			var selfType = LookupType(node.Semantic!.Type);
			var selfPtr = _builder.Handle.BuildAlloca(selfType, "self");
			_locals.Add("self", selfPtr);
			_builder.Handle.BuildStore(LLVM.ConstNull(selfType), selfPtr);
		}
		_currentCtorClass = node.IsConstructor && node.Semantic is Constructor { Owner.Type: InternalClass ic } ? ic : null;
		Visit(node.Body);
		_currentCtorClass = null;
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
		var name = Qualified(node.Name.Name);
		var func = _module.AddFunction(node.Name.Name, funcType);

		var extModule = node.GetAncestors<ModuleDeclaration>().FirstOrDefault(m => m.HasAnnotation("ExternalLibraryName"))
			?? throw new NotImplementedException();
		func.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLImportStorageClass;
		_functions.Add(name, (funcType, func));
	}

	private LLVMTypeRef BuildFuncType(IFunction func)
	{
		var isClassCtor = func is IMethod { ParentType: IClassType } && ((ISemantic)func).SemanticType.HasFlag(SemanticType.Constructor);
		var returnType = isClassCtor ? Types.Void : ((ISemantic)func).Type;
		return LLVMTypeRef.CreateFunction(
			LookupType(returnType),
			[.. func.Args.Select(p => LookupType(p.ParamType))],
			false
		);
	}

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

	private LLVMTypeRef BuildSpanType(SpanType type) => BuildSpanType(LookupType(type.ElementType), type.ElementType.Name);

	private LLVMTypeRef LookupType(IType type) => type switch {
		PrimitiveType pt => pt.Type,
		SpanType st => BuildSpanType(LookupType(st.ElementType), st.ElementType.Name),
		TypeSystem.ArrayType at => BuildSpanType(LookupType(at.ElementType), at.ElementType.Name),
		InlineArrayType ia => BuildInlineArrayType(LookupType(ia.ElementType), ia.Size!),
		InternalPrimitiveType => LLVMTypeRef.CreatePointer(_builtinTypes[type.Name], 0),
		RefType rt => LLVMTypeRef.CreatePointer(LookupType(rt.BaseType), 0),
		InternalStruct ist => GetStructType(ist),
		InternalClass icl => LLVMTypeRef.CreatePointer(GetClassStructType(icl), 0),
		IClassType => _builtinTypes["ptr"], // any other class: a pointer all the same
		NilableType nt => LookupType(nt.BaseType),
		MetaclassType => _builtinTypes["ptr"],
		_ => throw new NotImplementedException()
	};

	private LLVMTypeRef LookupCompositeType(IType type) => type is IClassType and not ObjectType ? GetClassStructType((IClassType)type) : LookupType(type);

	private static LLVMTypeRef BuildInlineArrayType(LLVMTypeRef baseType, Expression size)
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
		if (sem is ClassConstantDecl) {
			return; // Handled by GetOrCreateMetaclass
		}
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
		DescribeGlobal(field, sem, node.Position);
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
		DescribeGlobal(field, sem, node.Position);
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
		_lValueVisitor.Visit(node.Left);
		Debug.Assert(_values.Count == 1);
		var l = _values.Pop();
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
		if (node.Left is Identifier id && id.NodeType == NodeType.Identifier) {
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
			ArithmeticOperator.Add => node.Left.Semantic!.Type == Types.String ? Concatenate(l, r) : _builder.Handle.BuildAdd(l, r, "add"),
			ArithmeticOperator.Subtract => _builder.Handle.BuildSub(l, r, "sub"),
			ArithmeticOperator.Multiply => _builder.Handle.BuildMul(l, r, "mul"),
			ArithmeticOperator.Divide => _builder.Handle.BuildFDiv(l, r, "fdiv"),
			ArithmeticOperator.IDivide => _builder.Handle.BuildSDiv(l, r, "idiv"),
			ArithmeticOperator.Modulus => _builder.Handle.BuildSRem(l, r, "mod"),
			_ => throw new NotImplementedException()
		};
		_values.Push(result);
	}

	private LLVMValueRef Concatenate(LLVMValueRef l, LLVMValueRef r)
	{
		var name = _runtimeNamespace + "$$StringMethods$Concat";
		if (!_functions.TryGetValue(name, out var result)) {
			var type = LLVMTypeRef.CreateFunction(LookupType(Types.String), [LookupType(Types.String), LookupType(Types.String)], false);
			result = (type, _module.AddFunction(name, type));
			_functions.Add(name, result);
			_forwardDeclared.Add(name);
		}
		return _builder.Handle.BuildCall2(result.Type, result.Function, [l, r]);
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
			if (IsClassScope(node.Semantic)) {
				_values.Push(BuildMetaclassOperand(mi.ParentExpr));
			} else if (mi.ParentExpr.Semantic!.Type.IsStruct) {
				_lValueVisitor.Visit(mi.ParentExpr);
			} else {
				Visit(mi.ParentExpr);
			}
		}
		LLVMValueRef? receiverMetaclass = null;
		if ((node.Semantic is Constructor || IsExternalClassConstructor(node.Semantic))
			&& node.Target is MemberIdentifier { ParentExpr: { Semantic.Type: MetaclassType } receiver }) {
			Visit(receiver);
			receiverMetaclass = _values.Pop();
		}
		Visit(node.Args);
		Span<LLVMValueRef> args = stackalloc LLVMValueRef[node.Args.Length + selfCount];
		for (int i = 1; i <= args.Length; ++i) {
			args[^i] = _values.Pop();
		}
		if (IsExternalClassConstructor(node.Semantic)) {
			var externalCtor = (ExternalMethod)node.Semantic!;
			var metaclass = receiverMetaclass
				?? _builder.Handle.BuildBitCast(GetOrCreateMetaclass((IClassType)externalCtor.Parent), _builtinTypes["ptr"]);
			if (!_functions.TryGetValue(externalCtor.FullName, out var importedCtor)) {
				throw new CompileError(node, $"GetNamedFunction failed for '{externalCtor.FullName}'");
			}
			var newObj = GetNewObjectFunc();
			var self = _builder.Handle.BuildCall2(newObj.Type, newObj.Function, [metaclass]);
			LLVMValueRef[] ctorArgs = [self, .. args];
			var target = receiverMetaclass != null && externalCtor.Slot >= 0
				? BuildVmtLoadFromMetaclass(metaclass, externalCtor.Slot)
				: importedCtor.Function;
			_builder.Handle.BuildCall2(importedCtor.Type, target, ctorArgs);
			_values.Push(self);
			return;
		}
		if (node.Semantic is Constructor { Owner.Type: InternalClass cls } ctor) {
			if (!_functions.TryGetValue(ctor.FullName, out var ctorFunc)) {
				throw new CompileError(node, $"GetNamedFunction failed for '{ctor.FullName}'");
			}
			if (cls == _currentCtorClass
				&& (node.Target.NodeType == NodeType.Identifier || node.Target is MemberIdentifier { ParentExpr: SelfLiteralExpression })) {
				LLVMValueRef[] delegateArgs = [_currentFunc.FirstParam, .. args];
				_values.Push(_builder.Handle.BuildCall2(ctorFunc.Type, ctorFunc.Function, delegateArgs));
				return;
			}
			var metaclass = receiverMetaclass ?? _builder.Handle.BuildBitCast(GetOrCreateMetaclass(cls), _builtinTypes["ptr"]);
			var newObj = GetNewObjectFunc();
			var self = _builder.Handle.BuildCall2(newObj.Type, newObj.Function, [metaclass]);
			LLVMValueRef[] ctorArgs = [self, .. args];
			if (receiverMetaclass != null && ctor.Slot >= 0) {
				var fn = BuildVmtLoadFromMetaclass(metaclass, ctor.Slot);
				_builder.Handle.BuildCall2(ctorFunc.Type, fn, ctorArgs);
			} else {
				_builder.Handle.BuildCall2(ctorFunc.Type, ctorFunc.Function, ctorArgs);
			}
			_values.Push(self);
			return;
		}
		if (node.Semantic!.SemanticType.HasFlag(SemanticType.Magic)) {
			if (node.Semantic is ImplicitConstructor ic) {
				VisitImplicitConstructorCall(ic);
			} else {
				VisitMagicFunctionCall(node, args);
				return;
			}
		}
		var isVoid = node.Target.Semantic!.Type == Types.Void;
		if (node.Semantic is IMethod { ParentType: IClassType } method && method.Slot >= 0) {
			var fn = IsClassScope(method) ? BuildVmtLoadFromMetaclass(args[0], method.Slot) : BuildVmtLoad(args[0], method.Slot);
			var result = _builder.Handle.BuildCall2(GetVirtualMethodType(method), fn, args, isVoid ? [] : node.Target.ToString().AsSpan());
			_values.Push(result);
		} else {
			if (!_functions.TryGetValue(node.Semantic!.FullName, out var callee)) {
				throw new CompileError(node, $"GetNamedFunction failed for '{node.Semantic!.Name}'");
			}
			var result = _builder.Handle.BuildCall2(callee.Type, callee.Function, args, isVoid ? [] : node.Target.ToString().AsSpan());
			_values.Push(result);
		}
	}

	private void VisitImplicitConstructorCall(ImplicitConstructor ic)
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
			case "arr$ToPtr":
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
				var (funcType, func) = _functions[_runtimeNamespace + "$$Console$PrintLn"];
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
		var elemSize = LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, SizeOf(LookupType(arrType.ElementType)));

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
			case "string$Length":
				var ptr = _builder.Handle.BuildGEP2(LookupType(Types.String), parent, [LLVMValueRef.CreateConstInt(_context.Handle.Int32Type, 0)]);
				_values.Push(_builder.Handle.BuildLoad2(LookupType(Types.Int32), ptr, "strLength".AsSpan()));
				break;
			default:
				throw new NotImplementedException();
		}
	}

	private void VisitFieldDecl(FieldDecl fd, LLVMValueRef parent)
	{
		if (parent.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind) {
			var index = (uint)fd.Index + (fd.ParentType is ClassDeclaration ? 1u : 0u);
			var gep = _builder.Handle.BuildStructGEP2(LookupCompositeType(((IMemberSemantic)fd).ParentType), parent, index, $"{parent.Name}.{fd.Name}_gep");
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
			SpanType st => (_builder.Handle.BuildExtractValue(expr, 0, "spanPtr"), LookupType(st.ElementType)),
			TypeSystem.ArrayType at => (_builder.Handle.BuildExtractValue(expr, 0, "spanPtr"), LookupType(at.ElementType)),
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
		var elType = LookupType(ia.ElementType);
		var sem = node.Expr.Semantic;
		if (sem is VariableDecl vd) {
			var expr = _locals[vd.Name];
			VisitIndexing(expr, elType, node.Indices[0]);
		} else if (sem is FieldDecl fd) {
			Debug.Assert(node.Expr.NodeType == NodeType.MemberIdentifier);
			VisitFieldFlatIndexing((MemberIdentifier)node.Expr, fd, elType, node.Indices[0]);
		} else { 
			throw new NotImplementedException();
		}
	}

	private void VisitFieldFlatIndexing(MemberIdentifier expr, FieldDecl fd, LLVMTypeRef elType, Expression idxExpr)
	{
		var sem = expr.ParentExpr.Semantic;
		var parent = sem switch {
			GlobalFieldDecl gf => _globals[gf.FullName],
			_ => throw new NotImplementedException(),
		};
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
		_values.Push(result);
	}

	private LLVMValueRef BuildCast(IType type, IType targetType, LLVMValueRef expr, LLVMTypeRef typ)
	{
		if (type == targetType) {
			return expr;
		}
		if ((type is IClassType && targetType is IClassType) || (type is MetaclassType && targetType is MetaclassType)) {
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
			VisitNewObject(node);
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

	private void VisitNewObject(NewObjExpression node)
	{
		throw new NotImplementedException();
	}

	public override void VisitMemberIdentifier(MemberIdentifier node)
	{
		if (node.Semantic is IClassConstant constant) {
			VisitClassConstantRead(node, constant);
			return;
		}
		var count = _values.Count;
		Visit(node.ParentExpr);
		Debug.Assert(_values.Count == count + 1 || node.Semantic is GlobalFieldDecl
			|| node.ParentExpr.Semantic is { SemanticType: var parentKind } && (parentKind.HasFlag(SemanticType.Type) || parentKind.HasFlag(SemanticType.Namespace)));
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
				ptr = _globals[gf.FullName];
				_values.Push(_builder.Handle.BuildLoad2(LookupType(gf.Type), ptr, "var_" + gf.Name));
				break;
			case Semantics.Module:
			case StructDecl:
			case ClassDecl:
				break;
			case MagicProperty mp:
				var parent = _values.Pop();
				VisitMagicPropertyCall(mp, parent);
				break;
			case FieldDecl fd:
				parent = _values.Pop();
				VisitFieldDecl(fd, parent);
				break;
			case NamespaceRef:
			case TypeRef:
				break; // named on the way to something: nothing to evaluate
			case ExternalField { Parent: IClassType } ef:
				parent = _values.Pop();
				// The base class is the first element of a class's layout, so its own fields come after it.
				var fieldAddress = _builder.Handle.BuildStructGEP2(LookupCompositeType(ef.Parent), parent, (uint)ef.Index + 1, $"{parent.Name}.{ef.Name}_gep");
				_values.Push(_builder.Handle.BuildLoad2(LookupType(ef.Type), fieldAddress));
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

	public override void VisitBooleanLiteralExpression(BooleanLiteralExpression node)
		=> _values.Push(LLVMValueRef.CreateConstInt(_context.Handle.Int1Type, (ulong)(node.Value ? 1 : 0)));

	public override void VisitNilLiteralExpression(NilLiteralExpression node)
		=> _values.Push(LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(_context.Handle.VoidType, 0)));

	public override void VisitSelfLiteralExpression(SelfLiteralExpression node) => _values.Push(_isCtor ? _locals["self"] : _currentFunc.FirstParam);

	public override void VisitMetaclassExpression(MetaclassExpression node)
		=> _values.Push(_builder.Handle.BuildBitCast(GetOrCreateMetaclass((IClassType)node.Type.Semantic!.Type), _builtinTypes["ptr"]));

	public override void VisitSuperCallExpression(SuperCallExpression node)
	{
		var count = _values.Count;
		Visit(node.Args);
		Span<LLVMValueRef> args = stackalloc LLVMValueRef[node.Args.Length];
		for (int i = 1; i <= args.Length; ++i) {
			args[^i] = _values.Pop();
		}
		Debug.Assert(_values.Count == count);
		var self = _currentFunc.FirstParam;
		var callee = node.Semantic switch {
			RootObjectConstructor => GetObjectCreateFunc(),
			MethodDef m => _functions.TryGetValue(m.FullName, out var fn) ? fn : throw new CompileError(node, $"No function generated for '{m.FullName}'"),
			ExternalMethod em => _functions.TryGetValue(em.FullName, out var imported) ? imported : throw new CompileError(node, $"No function imported for '{em.FullName}'"),
			_ => throw new NotImplementedException("Unsupported function super call type"),
		};
		LLVMValueRef[] callArgs = [self, .. args];
		var result = _builder.Handle.BuildCall2(callee.Type, callee.Function, callArgs);
		if (_currentCtorClass != null) {
			EmitFieldDefaults(_currentCtorClass);
		}
		_values.Push(result);
	}

	private void EmitFieldDefaults(InternalClass cls)
	{
		if (!_fieldDefaults.TryGetValue(cls, out var fields)) {
			return;
		}
		foreach (var field in fields) {
			var self = new SelfLiteralExpression(field.Position) { Semantic = new TypeRef(cls) };
			var target = new MemberIdentifier(self, field.Decl.Name) { Semantic = field.Semantic };
			VisitAssignStatement(new AssignStatement(target, field.Value!, AssignOperator.Assign));
		}
	}
}
