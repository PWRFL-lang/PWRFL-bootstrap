using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class NewObjExpression(Position pos, TypeReference type, Expression[] args) : Expression(pos)
{
	public NewObjExpression(Position pos, TypeReference type, List<Expression> args)
		: this(pos, type, args.ToArray())
	{ }

	public TypeReference ObjectType { get; } = type;
	public Expression[] Args { get; } = args;

	public override NodeType Type => NodeType.NewObjExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitNewObjExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitNewObjExpression(this);
}
