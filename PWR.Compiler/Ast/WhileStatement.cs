using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class WhileStatement(Position pos, Expression cond, Statement[] body) : Statement(pos), IConditional
{
	public WhileStatement(Position pos, Expression cond, List<Statement> body)
		: this(pos, cond, body.ToArray())
	{ }

	public Expression Cond { get; } = cond;
	public Block Body { get; } = new(body);

	public override NodeType Type => NodeType.WhileStatement;

	public override void Accept(IVisitor visitor) => visitor.VisitWhileStatement(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitWhileStatement(this);

	Node IConditional.True => Body;
	Node? IConditional.False => null;
}
