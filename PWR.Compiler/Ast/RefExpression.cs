namespace PWR.Compiler.Ast;

public class RefExpression(Position position, Expression expr) : Expression(position)
{
	public Expression Expr { get; } = expr;

	public override NodeType Type => NodeType.RefExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitRefExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitRefExpression(this);
}
