using PWR.Compiler.TypeSystem;
using System;
using System.Runtime.InteropServices;

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
{ }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct TypeDefinition(
	int NamespaceRef,
	int NameRef,
	TypeOfType Type,
	TypeFlags Flags,
	Token ParentType,
	int AssociatedTypeRef) : IMetadataRow
{
	internal readonly string GetName(MetadataContext context) => context.GetString(NameRef);
	internal readonly string? GetNamespace(MetadataContext context) 
		=> NamespaceRef == 0 ? null : context.GetString(NamespaceRef);
	internal readonly IType? GetParentType(MetadataContext context)
		=> ParentType.IsNull ? null : throw new NotImplementedException();
}
