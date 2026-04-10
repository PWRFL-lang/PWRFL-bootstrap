using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

internal enum MethodAttributes : uint
{ }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct MethodDefinition(
	int OwnerRef,
	int NameRef,
	MethodAttributes Flags,
	int MethodSigRef,
	int Reserved
) : IMetadataRow
{ }
