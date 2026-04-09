using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

[InlineArray(MetadataContext.TABLE_COUNT)]
internal struct TableSizes
{
	private uint _value;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct MetadataHeader(
	// updated whenever a change to serialization is required
	// ie. new tables, new fields, changing the size of a field, etc.
	byte MajorVersion,

	// updated with changes that do not require a change in serialization
	// ie. renaming a field or putting a reserved field into use
	byte MinorVersion,

	// size in bytes of each metadata table
	TableSizes TableSizes);

