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

	protected abstract Token IdToken { get; }

	public string? Namespace { get; } = ns;
	public string Name { get; } = name;
	public IType? Parent { get; } = parent;
	internal ISemantic[]? Members { get; private set; }

	ISemantic? IType.GetMember(string name)
	{
		Members ??= LoadMembers();
		return Members.FirstOrDefault(m => m.Name == name);
	}

	private ISemantic[] LoadMembers()
	{
		var result = new List<ISemantic>();
		var tok = this.IdToken.AsInt;
		var idx = _context.FieldDefinitionTable.BinarySearchFirst(f => f.OwnerRef.AsInt, tok);
		if (idx >= 0) {
			foreach (var field in _context.FieldDefinitionTable.Skip(idx).TakeWhile(f => f.OwnerRef.AsInt == tok)) {
				result.Add(BuildField(field, this));
			}
		}

		idx = _context.MethodDefinitionTable.BinarySearchFirst(f => (int)f.OwnerRef, _rowIdx);
		if (idx >= 0) {
			for (int i = idx; i < _context.MethodDefinitionTable.Count; ++i) {
				var field = _context.MethodDefinitionTable[i];
				if (field.OwnerRef != _rowIdx) {
					break;
				}
				var paramIdx = _context.ParamDefinitionTable.BinarySearchFirst(p => p.OwnerRef, i);
				var names = _context.ParamDefinitionTable.Skip(paramIdx)
					.TakeWhile(p => p.OwnerRef == (uint)i)
					.Select(p => p.GetName(_context));
				result.Add(BuildMethod(field, names, this));
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

	private ExternalMethod BuildMethod(MethodDefinition func, IEnumerable<string> paramNames, IType owner)
	{
		var name = _context.GetString(func.NameRef);
		var sigBlob = _context.GetBlob(func.MethodSigRef);
		var sig = Signatures.ReadFunc(sigBlob, _context);
		return new ExternalMethod(name, sig, [.. paramNames], owner, func.Flags.HasFlag(MethodAttributes.HasSelf));
	}
}