namespace PWR.Compiler.Ast;

public class NilLiteralExpression(Position pos) : LiteralExpression(pos)
{
	public override NodeType Type => NodeType.NilLiteralExpression;

	public override object LiteralValue => null!;

	public override void Accept(IVisitor visitor) => visitor.VisitNilLiteralExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitNilLiteralExpression(this);
}
