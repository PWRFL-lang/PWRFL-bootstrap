using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Metadata;

// A class's metaclass RVA can't be written into the metadata bytes: it's resolved by the
// linker, from a relocation against the metaclass global. The metadata step leaves a zero
// placeholder at Offset, and codegen replaces those four bytes with the relocated value.
internal readonly record struct MetaclassFixup(int Offset, IClassType Class);
