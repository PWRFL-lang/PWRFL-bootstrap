using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class IfStatement(Position pos, Expression cond, Statement[] trueBlock, Statement[]? falseBlock)
	: Statement(pos)
{
	public IfStatement(Position pos, Expression cond, List<Statement> trueBlock, List<Statement>? falseBlock)
		: this(pos, cond, [.. trueBlock], falseBlock?.ToArray())
	{ }

	public Expression Cond { get; } = cond;
	public Statement[] TrueBlock { get; } = trueBlock;
	public Statement[]? FalseBlock { get; } = falseBlock;

	public override NodeType Type => NodeType.IfStatement;

	public override void Accept(IVisitor visitor) => visitor.VisitIfStatement(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitIfStatement(this);
}
