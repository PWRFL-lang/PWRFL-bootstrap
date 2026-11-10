using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

internal enum FieldFlags : ushort
{
	None = 0,
	// A `const` field: ValueRef holds its value.
	Const = 1,

	// virtual class-scope constants, which live in a VMT slot.
	VirtualConstant = 2,
	Abstract = 4,
	Override = 8,
	StorageMask = 15
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct FieldDefinition(
	Token OwnerRef,
	int NameRef,
	FieldFlags Flags,
	int TypeSigRef,
	// Blob offset of the value of a constant (see Signatures.WriteConstant), or 0 for none. Uses
	// the same encoding as a parameter's default value.
	int ValueRef,
	// The VMT slot of a virtual class-scope constant, or MetadataContext.NO_SLOT for any other field.
	int Slot) : IMetadataRow
{ }
