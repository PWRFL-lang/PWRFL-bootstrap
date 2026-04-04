namespace PWR.Compiler.Ast;

public class NewArrayExpression(Position pos, TypeReference type, Expression size) : Expression(pos)
{
	public TypeReference ArrayType { get; } = type;
	public Expression Size { get; } = size;

	public override NodeType Type => NodeType.NewArrayExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitNewArrayExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitNewArrayExpression(this);
}
