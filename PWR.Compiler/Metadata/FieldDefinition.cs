using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

internal enum FieldFlags : ushort
{ }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct FieldDefinition(Token OwnerRef, int NameRef, FieldFlags Flags, int TypeSigRef) : IMetadataRow
{ }
