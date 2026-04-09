using System.Runtime.InteropServices;

namespace PWR.Compiler.Metadata;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct TypeImport(Token Owner, uint NamespaceRef, uint NameRef): IMetadataRow
{ }
