using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public class TernaryExpression(Position pos, Expression cond, Expression l, Expression r)
	: Expression(pos), ITwoBranchExpression, IConditional
{
	public Expression Cond { get; } = cond;
	public Expression Left { get; } = l;
	public Expression Right { get; } = r;

	public override NodeType Type => NodeType.TernaryExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitTernaryExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitTernaryExpression(this);

	internal TernaryExpression With(Expression cond, Expression l, Expression r)
	{
		var result = new TernaryExpression(this.Position, cond, l, r);
		result.Semantic = Semantic == null ? null : new Ternary(result, Semantic.Type);
		return result;
	}

	Node IConditional.True => Left;
	Node? IConditional.False => Right;
}
