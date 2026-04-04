namespace PWR.Compiler.Ast;

public class ReturnStatement(Position pos, Expression? value) : Statement(pos)
{
	public Expression? Value { get; } = value;

	public override NodeType Type => NodeType.ReturnStatement;

	public override void Accept(IVisitor visitor) => visitor.VisitReturnStatement(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitReturnStatement(this);
}
