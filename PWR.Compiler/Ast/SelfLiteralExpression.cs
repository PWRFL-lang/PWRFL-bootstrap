namespace PWR.Compiler.Ast;

public class SelfLiteralExpression(Position pos) : Expression(pos)
{
	public override NodeType Type => NodeType.SelfLiteral;

	public override void Accept(IVisitor visitor) => visitor.VisitSelfLiteralExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitSelfLiteralExpression(this);

	public override string ToString() => "self";
}
