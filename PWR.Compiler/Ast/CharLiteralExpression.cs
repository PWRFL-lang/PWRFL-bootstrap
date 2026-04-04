namespace PWR.Compiler.Ast;

public class CharLiteralExpression(Position pos, char value) : LiteralExpression(pos)
{
	public char Value => value;
	public override object LiteralValue => value;

	public override NodeType Type => NodeType.CharLiteral;

	public override void Accept(IVisitor visitor) => visitor.VisitCharLiteralExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitCharLiteralExpression(this);
}
