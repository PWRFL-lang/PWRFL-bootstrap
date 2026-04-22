using System.Diagnostics;

using PWR.Compiler.Semantics;

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

	internal ComparisonExpression With(Expression l, Expression r)
	{
		var result = new ComparisonExpression(l, r, Operator);
		result.Semantic = new Comparison(result);
		return result;
	}

	public override NodeType Type => NodeType.ComparisonExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitComparisonExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitComparisonExpression(this);

	public override string ToString()
	{
		var oper = Operator switch {
			ComparisonOperator.Equals => "==",
			ComparisonOperator.NotEquals => "!=",
			ComparisonOperator.LessThan => "<",
			ComparisonOperator.GreaterThan => ">",
			ComparisonOperator.LessThanOrEqual => "<=",
			ComparisonOperator.GreaterThanOrEqual => ">=",
			_ => throw new UnreachableException()
		};
		return $"({Left} {oper} {Right})";
	}
}
