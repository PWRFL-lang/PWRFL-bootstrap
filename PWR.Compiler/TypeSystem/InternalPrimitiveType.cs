namespace PWR.Compiler.TypeSystem;

public abstract class InternalPrimitiveType(string name) : IType
{
	public string Name => name;
}
