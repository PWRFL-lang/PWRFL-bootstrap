namespace PWR.Compiler.Ast;

public class ExpressionStatement(Expression expr) : Statement(expr.Position)
{
	public Expression Expr { get; } = expr;
	public override NodeType Type => NodeType.ExpressionStatement;

	public override void Accept(IVisitor visitor) => visitor.VisitExpressionStatement(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitExpressionStatement(this);
}
