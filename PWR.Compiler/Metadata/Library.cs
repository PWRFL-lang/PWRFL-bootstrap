using System;
using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

[Flags]
internal enum LibraryFlags : uint
{ }

// A row of the library table. The first row is the current library the metadata belongs to; every
// subsequent roe is a dependency, with its expected version ID.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct Library(
	int NameRef,
	Guid ID,
	ushort MajorVersion,
	ushort MinorVersion,
	uint Revision,
	uint Build,
	LibraryFlags Flags
	) : IMetadataRow
{
	public const int IdOffset = sizeof(int);
}
