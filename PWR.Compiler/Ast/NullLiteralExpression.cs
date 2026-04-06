namespace PWR.Compiler.Ast;

public class NullLiteralExpression(Position pos) : LiteralExpression(pos)
{
	public override NodeType Type => NodeType.NullLiteralExpression;

	public override object LiteralValue => null!;

	public override void Accept(IVisitor visitor) => visitor.VisitNullLiteralExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitNullLiteralExpression(this);
}
