namespace PWR.Compiler.Ast;

public class BooleanLiteralExpression(Position position, bool value) : LiteralExpression(position)
{
	public bool Value { get; } = value;
	public override NodeType NodeType => NodeType.BooleanLiteral;
	public override object LiteralValue => Value;

	public override void Accept(IVisitor visitor) => visitor.VisitBooleanLiteralExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitBooleanLiteralExpression(this);
	public override string ToString() => Value.ToString();
}
