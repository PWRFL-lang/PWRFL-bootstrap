using PWR.Compiler.Ast;

namespace PWR.Compiler.TypeSystem;

public class InlineArrayType(IType baseType, Expression size) : IType, ICollectionType
{
	public IType BaseType { get; } = baseType;
	public Expression Size { get; } = size;

	public string Name => $"{BaseType}[{Size}]";
	public override string ToString() => Name;
}
