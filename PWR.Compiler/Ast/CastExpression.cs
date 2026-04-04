namespace PWR.Compiler.Ast;

public class CastExpression(Expression value, TypeReference cast) : Expression(value.Position)
{
	public Expression Value { get; } = value;
	public TypeReference CastType { get; } = cast;

	public override NodeType Type => NodeType.Cast;

	public override void Accept(IVisitor visitor) => visitor.VisitCastExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitCastExpression(this);
}
