using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class IndexingExpression(Expression expr, Expression[] indices) : Expression(expr.Position)
{
	public IndexingExpression(Expression expr, List<Expression> indices) : this(expr, indices.ToArray())
	{ }

	public Expression Expr { get; } = expr;
	public Expression[] Indices { get; } = [.. indices];

	public override NodeType Type => NodeType.IndexingExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitIndexingExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitIndexingExpression(this);
}
