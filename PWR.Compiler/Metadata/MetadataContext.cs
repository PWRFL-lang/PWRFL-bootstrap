using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using PWR.Compiler.Ast;
using PWR.Compiler.Helpers;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.External;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Metadata;

internal class MetadataContext : IDisposable
{
	//should always be the same as the number of metadata tables on the context
	public const int TABLE_COUNT = 6;

	public const byte MAJOR_VERSION = 0;
	public const byte MINOR_VERSION = 3;

	public const byte LIBRARY_ID = 1;
	public const byte TYPE_IMPORT_ID = 2;
	public const byte TYPE_DEF_ID = 3;
	public const byte FIELD_DEF_ID = 4;
	public const byte METHOD_DEF_ID = 5;
	public const byte PARAM_DEF_ID = 6;

	// The "owner" of a top-level function.
	public const int NO_OWNER = -1;

	// The slot of an element that isn't in a VMT.
	public const int NO_SLOT = -1;

	public List<Library> LibraryTable { get; } = [];
	public List<TypeImport> TypeImportTable { get; } = [];
	public List<TypeDefinition> TypeDefinitionTable { get; } = [];
	public List<FieldDefinition> FieldDefinitionTable { get; } = [];
	public List<MethodDefinition> MethodDefinitionTable { get; } = [];
	public List<ParamDefinition> ParamDefinitionTable { get; } = [];

	private readonly Dictionary<string, int> _stringCache = [];
	private readonly Dictionary<byte[], int> _blobCache = [];
	private readonly BinaryWriter _blobWriter;
	private readonly Dictionary<ISemantic, Token> _tokenCache = [];
	private readonly Dictionary<IType, Token> _typeCache = [];

	public MetadataContext()
	{
		var stream = new MemoryStream();
		_blobWriter = new(stream, System.Text.Encoding.UTF8, false);
		_blobWriter.Write((byte)0);
	}

	public MetadataContext(Stream stream)
	{
		using var reader = new PEReader(stream);
		var meta = reader.GetSectionData(".PWRMeta").GetContent().AsSpan();
		var blob = reader.GetSectionData(".PWRBlob").GetContent().AsSpan();
		var blobStream = new MemoryStream(blob.Length);
		_blobWriter = new(blobStream, System.Text.Encoding.UTF8, false);
		_blobWriter.Write(blob);
		Deserialize(meta);
	}

	public int AddString(string value)
	{
		if (!_stringCache.TryGetValue(value, out var result)) {
			result = (int)_blobWriter.BaseStream.Position;
			_stringCache.Add(value, result);
			_blobWriter.Write(value);
		}
		return result;
	}

	public int AddBlob(byte[] value)
	{
		if (!_blobCache.TryGetValue(value, out var result)) {
			result = (int)_blobWriter.BaseStream.Position;
			_blobCache.Add(value, result);
			_blobWriter.Write7BitEncodedInt(value.Length);
			_blobWriter.Write(value);
		}
		return result;
	}

	internal string GetString(int pos)
	{
		var bytes = ((MemoryStream)_blobWriter.BaseStream).GetBuffer().AsSpan(pos);
		var reader = new SpanReader(bytes);
		return reader.ReadString();
	}

	internal byte[] GetBlob(int pos) 
	{
		var bytes = ((MemoryStream)_blobWriter.BaseStream).GetBuffer().AsSpan(pos);
		var reader = new SpanReader(bytes);
		var len = reader.Read7BitEncodedInt();
		return reader.ReadBytes(len);
	}

	public (MetadataHeader Header, byte[] Metadata, byte[] Blob, List<MetaclassFixup> MetaclassFixups) Serialize()
	{
		// The library's own version ID is a hash of everything else, so it can't included in the hash.
		// This value gets filled in after the hash is computed.
		LibraryTable[0] = LibraryTable[0] with { ID = Guid.Empty };
		TableSizes sizes = default;
		using var metadataStream = new MemoryStream();
		uint lastSize = 0;
		WriteTable(LibraryTable, 0);
		WriteTable(TypeImportTable, 1);
		var typeDefStart = (int)metadataStream.Position;
		WriteTable(TypeDefinitionTable, 2);
		WriteTable(FieldDefinitionTable, 3);
		WriteTable(MethodDefinitionTable, 4);
		WriteTable(ParamDefinitionTable, 5);
		var header = new MetadataHeader(MAJOR_VERSION, MINOR_VERSION, sizes);

		var rowSize = System.Runtime.CompilerServices.Unsafe.SizeOf<TypeDefinition>();
		var fixups = new List<MetaclassFixup>();
		foreach (var (type, token) in _typeCache) {
			if (token.Type == TYPE_DEF_ID && TypeDefinitionTable[token.Value].Type == TypeOfType.Class) {
				fixups.Add(new(typeDefStart + token.Value * rowSize + TypeDefinition.MetaclassRvaOffset, (IClassType)type));
			}
		}
		fixups.Sort((a, b) => a.Offset.CompareTo(b.Offset));
		var metadata = metadataStream.ToArray();
		var blob = ((MemoryStream)_blobWriter.BaseStream).ToArray();
		var id = VersionId.Compute(GetString(LibraryTable[0].NameRef), MemoryMarshal.AsBytes(new ReadOnlySpan<MetadataHeader>(in header)), metadata, blob);
		LibraryTable[0] = LibraryTable[0] with { ID = id };
		// The library table is written first, and its first row is the library itself.
		id.TryWriteBytes(metadata.AsSpan(Library.IdOffset));
		return (header, metadata, blob, fixups);

		void WriteTable<T>(List<T> table, int idx) where T: struct {
			metadataStream.Write(MemoryMarshal.AsBytes(table.ToArray().AsSpan()));
			sizes[idx] = (uint)metadataStream.Position - lastSize;
			lastSize = (uint)metadataStream.Position;
		}
	}

	private void Deserialize(ReadOnlySpan<byte> meta)
	{
		var headers = MemoryMarshal.Cast<byte, MetadataHeader>(meta)[..1];
		var header = headers[0];
		var offset = MemoryMarshal.AsBytes(headers).Length;
		meta = meta[offset..];
		header.Deconstruct(out var major, out var minor, out var tableSizes);
		if (major != MAJOR_VERSION || minor != MINOR_VERSION) {
			throw new Exception("Invalid metadata header");
		}
		ReadTable(LibraryTable, 0, ref meta);
		ReadTable(TypeImportTable, 1, ref meta);
		ReadTable(TypeDefinitionTable, 2, ref meta);
		ReadTable(FieldDefinitionTable, 3, ref meta);
		ReadTable(MethodDefinitionTable, 4, ref meta);
		ReadTable(ParamDefinitionTable, 5, ref meta);

		void ReadTable<T>(List<T> table, int idx, ref ReadOnlySpan<byte> meta) where T: struct {
			offset = (int)tableSizes[idx];
			var values = MemoryMarshal.Cast<byte, T>(meta[..offset]);
			table.AddRange(values);
			meta = meta[offset..];
		}
	}

	internal void AddCurrentLibrary(string name)
	{
		var lib = new Library(AddString(name), Guid.Empty, 0, 1, 0, 0, default);
		LibraryTable.Add(lib);
	}

	internal void AddLibraryReference(string name, Guid id)
		=> LibraryTable.Add(new Library(AddString(name), id, 0, 1, 0, 0, default));

	internal void AddType(ISemantic typ, ISemantic? parent)
	{
		var parentTok = LookupSemantic(parent);
		var typeType = GetTypeType(typ);
		var vmtSlots = typ.Type is IClassType cls ? cls.VmtSlotCount : 0;
		var flags = typ.Type is IClassType { IsAbstract: true } ? TypeFlags.Abstract : TypeFlags.None;
		// AssociatedTypeRef and MetaclassRva are placeholders; see AddTypeAssociation and MetaclassFixup.
		var ns = typ switch { ClassDecl c => c.Namespace, Module m => m.Namespace, StructDecl s => s.Namespace, _ => null };
		var type = new TypeDefinition(ns == null ? 0 : AddString(ns), AddString(typ.Name), typeType, flags, parentTok, 0, 0, vmtSlots);
		var token = new Token(TYPE_DEF_ID, TypeDefinitionTable.Count);
		TypeDefinitionTable.Add(type);
		_tokenCache.Add(typ, token);
		if (typeType != TypeOfType.Module) {
			_typeCache.Add(typ.Type, token);
		}
	}

	internal void AddTypeAssociation(ISemantic typ)
	{
		IType? associated = typ switch {
			Module { Decl.ExtendType: { } et } => et.Semantic!.Type,
			_ when typ.Type is InternalClass { BaseType: InternalClass or ExternalClass } cls => cls.BaseType,
			_ => null
		};
		if (associated != null) {
			var row = _tokenCache[typ].Value;
			TypeDefinitionTable[row] = TypeDefinitionTable[row] with {
				AssociatedTypeRef = AddBlob(Signatures.WriteField(associated, this))
			};
		}
	}

	internal void AddField(ISemantic field, ISemantic? parent, FieldDeclaration? decl = null)
	{
		ArgumentNullException.ThrowIfNull(parent, nameof(parent));
		var parentTok = LookupSemantic(parent);
		Debug.Assert(parentTok.Type == TYPE_DEF_ID);
		// The reader binary-searches this table by owner, so rows must be added in owner order.
		Debug.Assert(FieldDefinitionTable.Count == 0 || FieldDefinitionTable[^1].OwnerRef.AsInt <= parentTok.AsInt);
		var sig = AddBlob(Signatures.WriteField(field.Type, this));
		var flags = FieldFlags.None;
		var valueRef = 0;
		var slot = NO_SLOT;
		if (decl is { VarType: VarUsage.Const }) {
			flags |= FieldFlags.Const;
			// A constant whose value the metadata can't encode is still recorded as a constant, just
			// without its value.
			if (decl.Value is { } value && value.IsLiteral) {
				valueRef = AddBlob(Signatures.WriteConstant(value, this));
			}
		}
		if (field is ClassConstantDecl classConstant) {
			flags |= FieldFlags.VirtualConstant;
			if (classConstant.Node.Flags.HasFlag(FunctionFlags.Abstract)) {
				flags |= FieldFlags.Abstract;
			}
			if (classConstant.Node.Flags.HasFlag(FunctionFlags.Override)) {
				flags |= FieldFlags.Override;
			}
			slot = ((IClassType)((IMemberSemantic)classConstant).ParentType).SlotOf(classConstant.Name);
		}
		var fld = new FieldDefinition(parentTok, AddString(field.Name), flags, sig, valueRef, slot);
		var token = new Token(FIELD_DEF_ID, FieldDefinitionTable.Count);
		FieldDefinitionTable.Add(fld);
		_tokenCache.Add(field, token);
	}

	internal void AddFunction(IFunction function, ISemantic? parent)
	{
		var owner = parent == null ? NO_OWNER : LookupSemantic(parent).Value;
		// The reader binary-searches this table by owner, so rows must be added in owner order.
		Debug.Assert(MethodDefinitionTable.Count == 0 || MethodDefinitionTable[^1].OwnerRef <= owner);
		var sig = AddBlob(Signatures.WriteMethod(function, this));
		var nameRef = AddString(((ISemantic)function).Name);
		var func = new MethodDefinition(owner, nameRef, AttributesOf(function), sig, function is IMethod method ? method.Slot : NO_SLOT);
		var token = new Token(METHOD_DEF_ID, MethodDefinitionTable.Count);
		MethodDefinitionTable.Add(func);
		_tokenCache.Add((ISemantic)function, token);
	}

	private static MethodAttributes AttributesOf(IFunction function)
	{
		var attributes = function.HasSelf ? MethodAttributes.HasSelf : MethodAttributes.None;
		if (function.Flags.HasFlag(FunctionFlags.Virtual)) {
			attributes |= MethodAttributes.Virtual;
		}
		if (function.Flags.HasFlag(FunctionFlags.Override)) {
			attributes |= MethodAttributes.Override;
		}
		if (function.Flags.HasFlag(FunctionFlags.Abstract)) {
			attributes |= MethodAttributes.Abstract;
		}
		if (function.Flags.HasFlag(FunctionFlags.ClassScope)) {
			attributes |= MethodAttributes.ClassScope;
		}
		if (function is ISemantic { SemanticType: var type } && type.HasFlag(SemanticType.Constructor)) {
			attributes |= MethodAttributes.Constructor;
		}
		return attributes;
	}

	internal void AddParam(ParamDef parameter, ISemantic? parent)
	{
		ArgumentNullException.ThrowIfNull(parent, nameof(parent));
		var parentTok = LookupSemantic(parent);
		Debug.Assert(parentTok.Type == METHOD_DEF_ID && parentTok.Value == MethodDefinitionTable.Count - 1);
		var defaultRef = parameter.Param.DefaultValue is { } defValue 
			? AddBlob(Signatures.WriteConstant(defValue))
			: 0;
		var param = new ParamDefinition(parentTok.Value, AddString(parameter.Name), (ushort)parameter.Position, default, defaultRef);
		var token = new Token(PARAM_DEF_ID, ParamDefinitionTable.Count);
		ParamDefinitionTable.Add(param);
		_tokenCache.Add(parameter, token);
	}

	private static TypeOfType GetTypeType(ISemantic sem) => sem switch {
		Module => TypeOfType.Module,
		StructDecl => TypeOfType.Value,
		ClassDecl => TypeOfType.Class,
		_ => throw new NotImplementedException()
	};

	private Token LookupSemantic(ISemantic? sem)
	{
		if (sem == null) {
			return default;
		}
		if (_tokenCache.TryGetValue(sem, out var result)) {
			return result;
		}
		throw new NotImplementedException();
	}

	internal IType?[]? ExternalTypes { get; set; }

	// Used when reading a library's metadata.  Finds a type in another library, by that library's
	// name and the type's namespace and name.
	internal Func<string, string?, string, IType>? ImportResolver { get; set; }

	internal IType ResolveType(int token)
	{
		var tok = new Token((byte)(token >> 24), token & 0x00FFFFFF);
		if (tok.Type == TYPE_DEF_ID && ExternalTypes is { } types && tok.Value < types.Length && types[tok.Value] is { } type) {
			return type;
		}
		if (tok.Type == TYPE_IMPORT_ID && tok.Value < TypeImportTable.Count) {
			var import = TypeImportTable[tok.Value];
			var library = GetString(LibraryTable[import.Owner.Value].NameRef);
			var resolver = ImportResolver ?? throw new Exception("Missing metadata import resolver.");
			return resolver(library, import.NamespaceRef == 0 ? null : GetString(import.NamespaceRef), GetString(import.NameRef));
		}
		throw new Exception($"Metadata refers to unknown type token 0x{token:X}.");
	}

	public Token LookupType(IType typ)
	{
		if (_typeCache.TryGetValue(typ, out var result)) {
			return result;
		}
		if (typ is ExternalType external) {
			return LookupImportedType(external);
		}
		throw new NotImplementedException();
	}

	private Token LookupImportedType(ExternalType type)
	{
		if (_importedTypes.TryGetValue(type, out var token)) {
			return token;
		}
		var library = LibraryTable.FindIndex(l => GetString(l.NameRef) == type.LibraryName);
		if (library <= 0) {
			throw new Exception($"'{type.Name}' is defined in unknown library '{type.LibraryName}'.");
		}
		TypeImportTable.Add(new TypeImport(new Token(LIBRARY_ID, library), type.Namespace == null ? 0 : AddString(type.Namespace), AddString(type.Name)));
		token = new Token(TYPE_IMPORT_ID, TypeImportTable.Count - 1);
		_importedTypes.Add(type, token);
		return token;
	}

	private readonly Dictionary<IType, Token> _importedTypes = [];

	public void Dispose() => _blobWriter.Dispose();
}
