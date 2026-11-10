using System;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.TypeSystem.External;

// A class in another library, read from its metadata.
internal class ExternalClass(string? ns, string name, IType? parent, MetadataContext context, int rowIdx)
	: ExternalType(ns, name, parent, context, rowIdx), IClassType
{
	private IClassType? _baseType;
	private VmtEntry[]? _vmt;

	protected override Token IdToken => new(MetadataContext.TYPE_DEF_ID, _rowIdx);

	private TypeDefinition Row => Context.TypeDefinitionTable[_rowIdx];

	public IClassType? BaseType => _baseType ??= ReadAssociatedType() switch {
		null => ObjectType.Instance,
		IClassType cls => cls,
		var other => throw new Exception($"Invalid metadata: the base of class '{Name}' is '{other}', which isn't a class."),
	};

	public bool IsAbstract => Row.Flags.HasFlag(TypeFlags.Abstract);

	// The instance fields declared on this class itself. (A class constant is a VMT slot, not a field.)
	public ISemantic[] Fields => [.. LoadedMembers.OfType<ExternalField>()];

	public IMethod[] Methods => [.. LoadedMembers.OfType<ExternalMethod>()];

	public VmtEntry[] Vmt => _vmt ??= BuildVmt();

	private VmtEntry[] BuildVmt()
	{
		var slotCount = Row.VmtSlotCount;
		var baseVmt = BaseType!.Vmt;
		if (baseVmt.Length > slotCount) {
			throw new Exception($"Invalid metadata: class '{Name}' has fewer VMT slots ({slotCount}) than its base class ({baseVmt.Length}).");
		}
		var table = new VmtEntry?[slotCount];
		for (var i = 0; i < baseVmt.Length; ++i) {
			table[i] = baseVmt[i];
		}
		foreach (var member in LoadedMembers) {
			switch (member) {
				case ExternalMethod { Slot: >= 0 } method:
					table[SlotInRange(method.Slot, method.Name)] = new VmtEntry(
						method.Name, method.FullName, method.Flags.HasFlag(FunctionFlags.Abstract), method.Flags.HasFlag(FunctionFlags.ClassScope));
					break;
				case ExternalClassConstant constant:
					table[SlotInRange(constant.Slot, constant.Name)] = new VmtEntry(
						constant.Name, constant.FullName, constant.IsAbstract, IsClassScope: false, IsConstant: true,
						ConstantType: constant.Type, Value: constant.Value);
					break;
			}
		}
		var gap = Array.FindIndex(table, e => e == null);
		if (gap >= 0) {
			throw new Exception($"Invalid metadata: nothing in class '{Name}' describes slot {gap} of its VMT.");
		}
		return [.. table.Select(e => e!.Value)];
	}

	private int SlotInRange(int slot, string member)
		=> slot < Row.VmtSlotCount
			? slot
			: throw new Exception($"Invalid metadata: '{member}' in class '{Name}' has slot {slot}, but the class has {Row.VmtSlotCount}.");

	// What the class declares, or else what its base class has, as in a class built from source (see
	// Inheritance).
	protected override ISemantic? FindMember(string name)
		=> base.FindMember(name) ?? Inheritance.Find(BaseType, name);

	public bool CanAssignTo(IType targetType)
	{
		if (targetType == this) {
			return true;
		}
		for (var current = BaseType; current != null; current = current.BaseType) {
			if (current == targetType) {
				return true;
			}
		}
		return false;
	}
}
