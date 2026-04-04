namespace PWR.Compiler.Ast;

public class StringLiteralExpression(Position position, string value) : LiteralExpression(position)
{
	public string Value { get; } = value;
	public override NodeType Type => NodeType.StringLiteral;
	public override object LiteralValue => Value;

	public override void Accept(IVisitor visitor) => visitor.VisitStringLiteralExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitStringLiteralExpression(this);
}
