using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class FunctionCallExpression(Identifier target, Expression[] args) : Expression(target.Position)
{
	public FunctionCallExpression(Identifier target, List<Expression> args) : this(target, args.ToArray())
	{ }

	public FunctionCallExpression With(Identifier target, Expression[] args)
		=> new(target, args) { Semantic = Semantic };

	public Identifier Target { get; } = target;
	public Expression[] Args { get; } = args;
	public override NodeType Type => NodeType.FunctionCall;

	public override void Accept(IVisitor visitor) => visitor.VisitFunctionCallExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitFunctionCallExpression(this);
}
