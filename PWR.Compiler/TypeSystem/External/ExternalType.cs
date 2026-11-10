using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Helpers;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem.External;

internal abstract class ExternalType(string? ns, string name, IType? parent, MetadataContext context, int rowIdx) : IType
{
	private readonly MetadataContext _context = context;
	protected readonly int _rowIdx = rowIdx;

	protected MetadataContext Context => _context;

	protected abstract Token IdToken { get; }

	public string LibraryName { get; internal set; } = "";

	public string? Namespace { get; } = ns;

	public string FullName => Namespace == null ? Name : $"{Namespace}$${Name}";
	public string Name { get; } = name;
	public IType? Parent { get; } = parent;
	internal ISemantic[]? Members { get; private set; }

	ISemantic? IType.GetMember(string name) => FindMember(name);

	protected virtual ISemantic? FindMember(string name) => LoadedMembers.FirstOrDefault(m => m.Name == name);

	protected ISemantic[] LoadedMembers => Members ??= LoadMembers();

	protected IType? ReadAssociatedType()
	{
		var reference = _context.TypeDefinitionTable[_rowIdx].AssociatedTypeRef;
		return reference == 0 ? null : Signatures.ReadField(_context.GetBlob(reference), _context);
	}

	private ISemantic[] LoadMembers()
	{
		var result = new List<ISemantic>();
		var tok = this.IdToken.AsInt;
		var idx = _context.FieldDefinitionTable.BinarySearchFirst(f => f.OwnerRef.AsInt, tok);
		if (idx >= 0) {
			// An instance field's position among the type's fields is where it is in the layout.
			var fieldIndex = 0;
			foreach (var field in _context.FieldDefinitionTable.Skip(idx).TakeWhile(f => f.OwnerRef.AsInt == tok)) {
				var built = BuildField(field, this, fieldIndex);
				if (built is ExternalField) {
					++fieldIndex;
				}
				result.Add(built);
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

	private ISemantic BuildField(FieldDefinition field, IType owner, int index)
	{
		var name = _context.GetString(field.NameRef);
		var sigBlob = _context.GetBlob(field.TypeSigRef);
		var type = Signatures.ReadField(sigBlob, _context);
		Expression? constant = field.Flags.HasFlag(FieldFlags.Const) && field.ValueRef != 0
			? Signatures.ReadConstant(_context.GetBlob(field.ValueRef), _context)
			: null;
		return field.Flags.HasFlag(FieldFlags.VirtualConstant)
			? new ExternalClassConstant(name, type, owner, field.Flags.HasFlag(FieldFlags.Abstract), field.Slot, constant)
			: new ExternalField(name, type, owner, constant, index);
	}

	private static FunctionFlags FunctionFlagsOf(MethodAttributes attributes)
	{
		var flags = FunctionFlags.None;
		if (attributes.HasFlag(MethodAttributes.Virtual)) {
			flags |= FunctionFlags.Virtual;
		}
		if (attributes.HasFlag(MethodAttributes.Override)) {
			flags |= FunctionFlags.Override;
		}
		if (attributes.HasFlag(MethodAttributes.Abstract)) {
			flags |= FunctionFlags.Abstract;
		}
		if (attributes.HasFlag(MethodAttributes.ClassScope)) {
			flags |= FunctionFlags.ClassScope;
		}
		return flags;
	}

	private ExternalMethod BuildMethod(MethodDefinition func, IEnumerable<string> paramNames, IType owner)
	{
		var name = _context.GetString(func.NameRef);
		var sigBlob = _context.GetBlob(func.MethodSigRef);
		var sig = Signatures.ReadFunc(sigBlob, _context);
		var isConstructor = func.Flags.HasFlag(MethodAttributes.Constructor) && owner is IClassType;
		return new ExternalMethod(name, sig, [.. paramNames], owner, func.Flags.HasFlag(MethodAttributes.HasSelf),
			FunctionFlagsOf(func.Flags), func.Slot, isConstructor);
	}

	public override string ToString() => Name;
}
