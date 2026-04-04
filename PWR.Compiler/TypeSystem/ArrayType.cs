namespace PWR.Compiler.TypeSystem;

public class ArrayType(IType baseType) : IType, ICollectionType
{
	public IType BaseType { get; } = baseType;

	public string Name => BaseType.Name + " array";

	public IType MakeArray()
	{
		throw new System.NotImplementedException();
	}

	public IType MakeSpan()
	{
		throw new System.NotImplementedException();
	}
}
