namespace PWR.Compiler.Ast;

public enum ComparisonOperator
{
	Equals,
	NotEquals,
	LessThan,
	GreaterThan,
	LessThanOrEqual,
	GreaterThanOrEqual,
}

public class ComparisonExpression(Expression l, Expression r, ComparisonOperator op) : Expression(l.Position), ITwoBranchExpression
{
	public Expression Left { get; } = l;
	public Expression Right { get; } = r;
	public ComparisonOperator Operator { get; } = op;

	public override NodeType Type => NodeType.ComparisonExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitComparisonExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitComparisonExpression(this);
}
