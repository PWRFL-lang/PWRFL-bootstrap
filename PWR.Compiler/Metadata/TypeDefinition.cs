using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Metadata;

internal enum TypeOfType: ushort
{
	Value,
	Class,
	Module,
	Interface,
	Shape,
	FunctionReference,
	Metaclass
}

[Flags]
internal enum TypeFlags: uint
{
	None = 0,
	Abstract = 1,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct TypeDefinition(
	int NamespaceRef,
	int NameRef,
	TypeOfType Type,
	TypeFlags Flags,
	Token ParentType,
	int AssociatedTypeRef,
	uint MetaclassRva,
	int VmtSlotCount) : IMetadataRow
{
	internal static int MetaclassRvaOffset => Unsafe.SizeOf<TypeDefinition>() - sizeof(uint) - sizeof(int);

	internal readonly string GetName(MetadataContext context) => context.GetString(NameRef);
	internal readonly string? GetNamespace(MetadataContext context) 
		=> NamespaceRef == 0 ? null : context.GetString(NamespaceRef);
	internal readonly IType? GetParentType(MetadataContext context)
		=> ParentType.IsNull ? null : throw new NotImplementedException();
}
