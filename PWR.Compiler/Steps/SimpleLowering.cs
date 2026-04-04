using PWR.Compiler.Ast;

namespace PWR.Compiler.Steps;

public class SimpleLowering : TransformerCompileStep
{
	public override Node? VisitExpressionStatement(ExpressionStatement node)
	{
		var expr = VisitExpression(node.Expr)!;
		switch (expr.Type) {
			case NodeType.ArithmeticExpression:
				var ae = (ArithmeticExpression)expr;
				if (ae.IsAssign) {
					return new AssignStatement(ae);
				}
				break;
			case NodeType.UnaryExpression:
				var ue = (UnaryExpression)expr;
				if (ue.Operator is UnaryOperator.Inc or UnaryOperator.Dec) {
					return new AssignStatement(ue);
				}
				break;
		}
		return expr == node.Expr ? node : new ExpressionStatement(expr);
	}

	public override Node? VisitUnaryExpression(UnaryExpression node)
	{
		var value = VisitExpression(node.Expr)!;
		if (value.Type == NodeType.IntegerLiteral) {
			return node.Operator switch {
				UnaryOperator.Plus => value,
				UnaryOperator.Minus => new IntegerLiteralExpression(node.Position, -((IntegerLiteralExpression)value).Value),
				UnaryOperator.Inc or UnaryOperator.Dec => throw new CompileError(node, "The ++ and -- operators cannot be used with constant values"),
				_ => throw new System.NotImplementedException(),
			};
		}
		return value == node.Expr ? node : new UnaryExpression(node.Position, value, node.Operator);
	}
}
