namespace PWR.Compiler.TypeSystem;

// placeholder type to get things bootstrapped. Will be removed once generics are implemented
internal class SequenceType(IType baseType) : IType, ICollectionType
{
	public IType BaseType => baseType;

	public string Name => BaseType.Name + " seq";

	public IType MakeArray()
	{
		throw new System.NotImplementedException();
	}

	public IType MakeSpan()
	{
		throw new System.NotImplementedException();
	}
}
