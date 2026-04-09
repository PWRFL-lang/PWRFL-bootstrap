using System;
using System.Collections.Generic;
using System.Linq;
using PWR.Compiler.Helpers;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem.External;

internal abstract class ExternalType(string? ns, string name, IType? parent, MetadataContext context, int rowIdx) : IType
{
	private readonly MetadataContext _context = context;
	protected readonly int _rowIdx = rowIdx;
	private ISemantic[]? _members;

	protected abstract Token IdToken { get; }

	public string? Namespace { get; } = ns;
	public string Name { get; } = name;
	public IType? Parent { get; } = parent;

	ISemantic? IType.GetMember(string name)
	{
		_members ??= LoadMembers();
		return _members.FirstOrDefault(m => m.Name == name);
	}

	private ISemantic[] LoadMembers()
	{
		var result = new List<ISemantic>();
		var tok = this.IdToken.AsUInt;
		var idx = _context.FieldDefinitionTable.BinarySearchFirst(f => f.OwnerRef.AsUInt, tok);
		if (idx >= 0) {
			foreach (var field in _context.FieldDefinitionTable.Skip(idx).TakeWhile(f => f.OwnerRef.AsUInt == tok)) {
				result.Add(BuildField(field, this));
			}
		}

		idx = _context.MethodDefinitionTable.BinarySearchFirst(f => (int)f.OwnerRef, _rowIdx);
		if (idx >= 0) {
			foreach (var field in _context.MethodDefinitionTable.Skip(idx).TakeWhile(f => f.OwnerRef == _rowIdx)) {
				result.Add(BuildMethod(field, this));
			}
		}
		return [.. result];
	}

	private ExternalField BuildField(FieldDefinition field, IType owner)
	{
		var name = _context.GetString(field.NameRef);
		var sigBlob = _context.GetBlob(field.TypeSigRef);
		var type = Signatures.ReadField(sigBlob, _context);
		return new ExternalField(name, type, owner);
	}

	private ExternalMethod BuildMethod(MethodDefinition func, IType owner)
	{
		var name = _context.GetString(func.NameRef);
		var sigBlob = _context.GetBlob(func.MethodSigRef);
		var sig = Signatures.ReadFunc(sigBlob, _context);
		return new ExternalMethod(name, sig, owner);
	}
}