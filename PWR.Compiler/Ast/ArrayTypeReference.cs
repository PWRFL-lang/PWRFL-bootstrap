namespace PWR.Compiler.Ast;

public class ArrayTypeReference(TypeReference baseType, Expression? size) : TypeReference(baseType.Position)
{
	public TypeReference BaseType { get; } = baseType;
	public Expression? Size { get; } = size;

	public override NodeType Type => NodeType.ArrayTypeReference;

	public override void Accept(IVisitor visitor) => visitor.VisitArrayTypeReference(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitArrayTypeReference(this);
}
