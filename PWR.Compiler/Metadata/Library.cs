using System;
using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

[Flags]
internal enum LibraryFlags : uint
{ }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct Library(
	uint NameRef,
	Guid ID,
	ushort MajorVersion,
	ushort MinorVersion,
	uint Revision,
	uint Build,
	LibraryFlags Flags
	) : IMetadataRow
{ }
