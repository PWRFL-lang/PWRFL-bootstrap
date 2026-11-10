namespace PWR.Compiler.TypeSystem;

public interface IClassType : ICompositeType
{
	IClassType? BaseType { get; }
	IMethod[] Methods { get; }

	// The class's VMT, one entry per slot, filled in by the AssignVmtSlots step. The base's
	// entries are a prefix of the derived class's; an override replaces the entry for the slot
	// it reuses, and a class's own new virtual members are appended after the inherited ones.
	VmtEntry[] Vmt { get; }

	int VmtSlotCount => Vmt.Length;

	// Declared `class abstract`: it may leave members unimplemented, so it can't be instantiated.
	bool IsAbstract => false;

	// The slot for a virtual member of this class (declared here or inherited), or -1 if the
	// name isn't virtual.
	int SlotOf(string name) => System.Array.FindIndex(Vmt, e => e.Name == name);
}