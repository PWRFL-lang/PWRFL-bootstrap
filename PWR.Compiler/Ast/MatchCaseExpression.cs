using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class MatchCaseExpression(Position pos, Expression[]? cases, Expression value) : Expression(pos)
{
	public MatchCaseExpression(Position pos, List<Expression>? cases, Expression value)
		: this(pos, cases?.ToArray(), value)
	{ }

	public Expression[]? Cases { get; } = cases;
	public Expression Value { get; } = value;

	public override NodeType Type => NodeType.MatchCaseExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitMatchCaseExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitMatchCaseExpression(this);
}