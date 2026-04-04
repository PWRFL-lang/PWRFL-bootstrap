using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class MatchExpression(Position pos, Expression value, MatchCaseExpression[] cases) : Expression(pos)
{
	public MatchExpression(Position pos, Expression value, List<MatchCaseExpression> cases)
		: this(pos, value, cases.ToArray())
	{ }

	public Expression Value { get; } = value;
	public MatchCaseExpression[] Cases { get; } = cases;

	public override NodeType Type => NodeType.MatchExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitMatchExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitMatchExpression(this);
}
