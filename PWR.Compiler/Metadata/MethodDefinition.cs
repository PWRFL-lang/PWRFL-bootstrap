using System;
using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

[Flags]
internal enum MethodAttributes : uint
{
	None = 0,
	HasSelf = 1,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct MethodDefinition(
	int OwnerRef,
	int NameRef,
	MethodAttributes Flags,
	int MethodSigRef,
	int Reserved
) : IMetadataRow
{ }
