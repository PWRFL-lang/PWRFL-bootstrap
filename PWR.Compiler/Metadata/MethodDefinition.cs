using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

internal enum MethodAttributes : uint
{ }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct MethodDefinition(
	uint OwnerRef,
	uint NameRef,
	MethodAttributes Flags,
	uint MethodSigRef,
	uint Reserved
) : IMetadataRow
{ }
