namespace PWR.Compiler.TypeSystem;

public class InternalPrimitiveType(string name) : IType
{
	public string Name => name;

	public IType MakeArray() => new ArrayType(this);

	public IType MakeSpan() => new SpanType(this);
}
