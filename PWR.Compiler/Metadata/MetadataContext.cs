using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using PWR.Compiler.Helpers;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Metadata;

internal class MetadataContext : IDisposable
{
	//should always be the same as the number of metadata tables on the context
	public const int TABLE_COUNT = 6;

	public const byte LIBRARY_ID = 1;
	public const byte TYPE_IMPORT_ID = 2;
	public const byte TYPE_DEF_ID = 3;
	public const byte FIELD_DEF_ID = 4;
	public const byte METHOD_DEF_ID = 5;
	public const byte PARAM_DEF_ID = 6;

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

	public (MetadataHeader Header, byte[] Metadata, byte[] Blob) Serialize()
	{
		TableSizes sizes = default;
		using var metadataStream = new MemoryStream();
		uint lastSize = 0;
		WriteTable(LibraryTable, 0);
		WriteTable(TypeImportTable, 1);
		WriteTable(TypeDefinitionTable, 2);
		WriteTable(FieldDefinitionTable, 3);
		WriteTable(MethodDefinitionTable, 4);
		WriteTable(ParamDefinitionTable, 5);
		var header = new MetadataHeader(0, 1, sizes);
		return (header, metadataStream.ToArray(), ((MemoryStream)_blobWriter.BaseStream).ToArray());

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
		if (major != 0 || minor != 1) {
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

	internal void AddLibrary(string name)
	{
		var lib = new Library(AddString(name), Guid.NewGuid(), 0, 1, 0, 0, default);
		LibraryTable.Add(lib);
	}

	internal void AddType(ISemantic typ, ISemantic? parent)
	{
		var parentTok = LookupSemantic(parent);
		var typeType = GetTypeType(typ);
		var assoc = typ switch {
			Module { Decl.ExtendType: { } et } => AddBlob(Signatures.WriteField(et.Semantic!.Type, this)),
			_ => 0
		};
		var type = new TypeDefinition(0, AddString(typ.Name), typeType, default, parentTok, assoc);
		var token = new Token(TYPE_DEF_ID, TypeDefinitionTable.Count);
		TypeDefinitionTable.Add(type);
		_tokenCache.Add(typ, token);
		if (typeType != TypeOfType.Module) {
			_typeCache.Add(typ.Type, token);
		}
	}

	internal void AddField(ISemantic field, ISemantic? parent)
	{
		ArgumentNullException.ThrowIfNull(parent, nameof(parent));
		Debug.Assert(
			_tokenCache.TryGetValue(parent, out var parentTok)
			&& parentTok.Type == TYPE_DEF_ID
			&& parentTok.Value == TypeDefinitionTable.Count - 1);
		var sig = AddBlob(Signatures.WriteField(field.Type, this));
		var fld = new FieldDefinition(parentTok, AddString(field.Name), default, sig);
		var token = new Token(FIELD_DEF_ID, FieldDefinitionTable.Count);
		FieldDefinitionTable.Add(fld);
		_tokenCache.Add(field, token);
	}

	internal void AddFunction(IFunction function, ISemantic? parent)
	{
		var parentTok = LookupSemantic(parent);
		var sig = AddBlob(Signatures.WriteMethod(function, this));
		var nameRef = AddString(((ISemantic)function).Name);
		var func = new MethodDefinition(parentTok.Value, nameRef, function.HasSelf ? MethodAttributes.HasSelf : default, sig, default);
		var token = new Token(METHOD_DEF_ID, MethodDefinitionTable.Count);
		MethodDefinitionTable.Add(func);
		_tokenCache.Add((ISemantic)function, token);
	}

	internal void AddParam(ParamDef parameter, ISemantic? parent)
	{
		ArgumentNullException.ThrowIfNull(parent, nameof(parent));
		Debug.Assert(
			_tokenCache.TryGetValue(parent, out var parentTok)
			&& parentTok.Type == METHOD_DEF_ID
			&& parentTok.Value == MethodDefinitionTable.Count - 1);
		var param = new ParamDefinition(parentTok.Value, AddString(parameter.Name), (ushort)parameter.Position, default);
		var token = new Token(PARAM_DEF_ID, ParamDefinitionTable.Count);
		ParamDefinitionTable.Add(param);
		_tokenCache.Add(parameter, token);
	}

	private TypeOfType GetTypeType(ISemantic sem) => sem switch {
		Module => TypeOfType.Module,
		StructDecl => TypeOfType.Value,
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

	public Token LookupType(IType typ)
	{
		if (_typeCache.TryGetValue(typ, out var result)) {
			return result;
		}
		throw new NotImplementedException();
	}

	public void Dispose() => _blobWriter.Dispose();
}
