namespace PWR.Compiler.Ast;

public enum UnaryOperator
{
	Plus,
	Minus,
	Inc,
	Dec
}

public class UnaryExpression(Position pos, Expression expr, UnaryOperator op) : Expression(pos)
{
	public Expression Expr { get; } = expr;
	public UnaryOperator Operator { get; } = op;

	public override NodeType Type => NodeType.UnaryExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitUnaryExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitUnaryExpression(this);
}
