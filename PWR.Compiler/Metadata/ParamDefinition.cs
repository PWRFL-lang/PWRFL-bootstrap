using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

internal enum ParamAttributes : ushort
{ }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct ParamDefinition(uint NameRef, ushort Index, ParamAttributes Attributes) : IMetadataRow
{ }
