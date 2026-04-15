using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class FunctionCallExpression(Expression target, Expression[] args) : Expression(target.Position)
{
	public FunctionCallExpression(Expression target, List<Expression> args) : this(target, args.ToArray())
	{ }

	public FunctionCallExpression With(Expression target, Expression[] args)
		=> new(target, args) { Semantic = Semantic };

	public Expression Target { get; } = target;
	public Expression[] Args { get; } = args;
	public override NodeType Type => NodeType.FunctionCall;

	public override void Accept(IVisitor visitor) => visitor.VisitFunctionCallExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitFunctionCallExpression(this);
}
