namespace PWR.Compiler.Ast;

public class CastExpression(Position pos, Expression value, TypeReference cast) : Expression(pos)
{
	public CastExpression(Expression value, TypeReference cast) : this(value.Position, value, cast)
	{ }

	public Expression Value { get; } = value;
	public TypeReference CastType { get; } = cast;

	public override NodeType Type => NodeType.Cast;

	public override void Accept(IVisitor visitor) => visitor.VisitCastExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitCastExpression(this);

	public override string ToString() => $"cast {Value}: {CastType}";
}
