using System;
using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

[Flags]
internal enum MethodAttributes : uint
{
	None = 0,
	HasSelf = 1,
	Virtual = 2,
	Override = 4,
	Abstract = 8,
	ClassScope = 16,
	Constructor = 32,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct MethodDefinition(
	int OwnerRef,
	int NameRef,
	MethodAttributes Flags,
	int MethodSigRef,
	// The method's VMT slot, or MetadataContext.NO_SLOT if it doesn't have one.
	int Slot
) : IMetadataRow
{ }
