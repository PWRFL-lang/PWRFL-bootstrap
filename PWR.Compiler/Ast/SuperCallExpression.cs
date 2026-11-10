using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class SuperCallExpression(Position pos, Identifier name, Expression[] args) : Expression(pos)
{
	public SuperCallExpression(Position pos, Identifier name, List<Expression> args) : this(pos, name, args.ToArray())
	{ }

	public Identifier Name { get; } = name;
	public Expression[] Args { get; } = args;
	public override NodeType NodeType => NodeType.SuperCallExpression;

	public SuperCallExpression With(Expression[] args) => new(Position, Name, args) { Semantic = Semantic };

	public override void Accept(IVisitor visitor) => visitor.VisitSuperCallExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitSuperCallExpression(this);

	public override string ToString() => $"super {Name}({string.Join(", ", Args)})";
}
