namespace PWR.Compiler.TypeSystem;

// placeholder type to get things bootstrapped. Will be removed once generics are implemented
internal class SequenceType(IType baseType) : IType, ICollectionType
{
	public IType ElementType => baseType;

	public string Name => ElementType.Name + " seq";
}
