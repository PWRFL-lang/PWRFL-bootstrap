using System;
using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

internal enum ParamAttributes : ushort
{ }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct ParamDefinition(int OwnerRef, int NameRef, ushort Index, ParamAttributes Attributes) : IMetadataRow
{
	internal readonly string GetName(MetadataContext context) => context.GetString(NameRef);
}
