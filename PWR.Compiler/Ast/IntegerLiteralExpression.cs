namespace PWR.Compiler.Ast;

public class IntegerLiteralExpression(Position position, int value) : LiteralExpression(position)
{
	public int Value { get; } = value;
	public override NodeType Type => NodeType.IntegerLiteral;
	public override object LiteralValue => Value;

	public override void Accept(IVisitor visitor) => visitor.VisitIntegerLiteralExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitIntegerLiteralExpression(this);
}
