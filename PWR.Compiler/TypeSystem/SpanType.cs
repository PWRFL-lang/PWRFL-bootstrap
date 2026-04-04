namespace PWR.Compiler.TypeSystem;

public class SpanType(IType baseType) : IType, ICollectionType
{
	public IType BaseType { get; } = baseType;

	public string Name => BaseType.Name + " span";

	public IType MakeArray()
	{
		throw new System.NotImplementedException();
	}

	public IType MakeSpan()
	{
		throw new System.NotImplementedException();
	}
}
