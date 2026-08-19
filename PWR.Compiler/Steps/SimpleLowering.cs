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
		if (value == node.Expr && node.Operator is UnaryOperator.Inc or UnaryOperator.Dec) {
			return node;
		}
		if (node.Operator == UnaryOperator.Plus) {
			return value;
		}
		if (node.Operator == UnaryOperator.Minus && value.IsLiteral) {
			return Negate((LiteralExpression)value);
		}
		if (value.IsLiteral && node.Operator is UnaryOperator.Inc or UnaryOperator.Dec) {
			throw new CompileError(node, "The ++ and -- operators cannot be used with constant values");
		}
		return value == node.Expr ? node : new UnaryExpression(node.Position, value, node.Operator);
	}

	private static LiteralExpression Negate(LiteralExpression value) => value switch {
		IntegerLiteralExpression il => new IntegerLiteralExpression(il.Position, -il.Value),
		_ => throw new CompileError(value, $"Unable to negate '{value.LiteralValue}'")
	};

	public override Node? VisitArithmeticExpression(ArithmeticExpression node)
	{
		var l = VisitExpression(node.Left)!;
		var r = VisitExpression(node.Right)!;
		if (l == node.Left && r == node.Right && !(l.IsLiteral && r.IsLiteral)) {
			return node;
		}
		if (l.IsLiteral && r.IsLiteral) {
			return Calculate((LiteralExpression)l, (LiteralExpression)r, node.Operator);
		}
		return new ArithmeticExpression(l, r, node.Operator);
	}

	private static Expression Calculate(LiteralExpression l, LiteralExpression r, ArithmeticOperator op) => op switch {
		ArithmeticOperator.Add => Add(l, r),
		ArithmeticOperator.Subtract => Subtract(l, r),
		ArithmeticOperator.Multiply => Multiply(l, r),
		ArithmeticOperator.IDivide => IDivide(l, r),
		ArithmeticOperator.Modulus => Modulus(l, r),
		_ => new ArithmeticExpression(l, r, op),
	};

	private static LiteralExpression Add(LiteralExpression l, LiteralExpression r) => l switch {
		IntegerLiteralExpression il1 => r switch {
			IntegerLiteralExpression il2 => new IntegerLiteralExpression(il1.Position, il1.Value + il2.Value),
			CharLiteralExpression cl => new CharLiteralExpression(il1.Position, (char)(cl.Value + il1.Value)),
			_ => throw new CompileError(l, $"Cannot add '{l}' and '{r}'")
		},
		CharLiteralExpression cl1 => r switch {
			CharLiteralExpression cl2 => new StringLiteralExpression(cl1.Position, $"{cl1.Value}{cl2.Value}"),
			StringLiteralExpression sl => new StringLiteralExpression(cl1.Position, $"{cl1.Value}{sl.Value}"),
			IntegerLiteralExpression il => new CharLiteralExpression(cl1.Position, (char)(cl1.Value + il.Value)),
			_ => throw new CompileError(l, $"Cannot add '{l}' and '{r}'")
		},
		StringLiteralExpression sl1 => r switch {
			CharLiteralExpression cl => new StringLiteralExpression(sl1.Position, $"{sl1.Value}{cl.Value}"),
			StringLiteralExpression sl2 => new StringLiteralExpression(sl1.Position, $"{sl1.Value}{sl2.Value}"),
			_ => throw new CompileError(l, $"Cannot add '{l}' and '{r}'")
		},
		_ => throw new CompileError(l, $"Cannot add '{l}' and '{r}'")
	};

	private static LiteralExpression Subtract(LiteralExpression l, LiteralExpression r) => l switch
	{
		IntegerLiteralExpression il1 => r switch {
			IntegerLiteralExpression il2 => new IntegerLiteralExpression(il1.Position, il1.Value - il2.Value),
			CharLiteralExpression cl => new CharLiteralExpression(il1.Position, (char)(cl.Value - il1.Value)),
			_ => throw new CompileError(l, $"Cannot subtract '{r}' from '{l}'")
		},
		CharLiteralExpression cl1 => r switch {
			IntegerLiteralExpression il => new CharLiteralExpression(cl1.Position, (char)(cl1.Value - il.Value)),
			_ => throw new CompileError(l, $"Cannot subtract '{r}' from '{l}'")
		},
		_ => throw new CompileError(l, $"Cannot subtract '{r}' from '{l}'")
	};

	private static LiteralExpression Multiply(LiteralExpression l, LiteralExpression r) => l switch {
		IntegerLiteralExpression il1 => r switch {
			IntegerLiteralExpression il2 => new IntegerLiteralExpression(il1.Position, il1.Value * il2.Value),
			_ => throw new CompileError(l, $"Cannot multiply '{l}' and '{r}'")
		},
		_ => throw new CompileError(l, $"Cannot multiply '{l}' and '{r}'")
	};

	private static LiteralExpression IDivide(LiteralExpression l, LiteralExpression r) => l switch {
		IntegerLiteralExpression il1 => r switch {
			IntegerLiteralExpression il2 => new IntegerLiteralExpression(il1.Position, il1.Value / il2.Value),
			_ => throw new CompileError(l, $"Cannot divide '{l}' by '{r}'")
		},
		_ => throw new CompileError(l, $"Cannot divide '{l}' by '{r}'")
	};

	private static LiteralExpression Modulus(LiteralExpression l, LiteralExpression r) => l switch {
		IntegerLiteralExpression il1 => r switch {
			IntegerLiteralExpression il2 => new IntegerLiteralExpression(il1.Position, il1.Value % il2.Value),
			_ => throw new CompileError(l, $"Cannot divide '{l}' by '{r}'")
		},
		_ => throw new CompileError(l, $"Cannot divide '{l}' by '{r}'")
	};
}
