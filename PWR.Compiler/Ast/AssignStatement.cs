using System;

namespace PWR.Compiler.Ast;

public enum AssignOperator
{
	Assign,
	InPlaceAdd,
	InPlaceSub,
	InPlaceMul,
	InPlaceDiv,
	InPlaceIDiv,
}

public class AssignStatement(Expression l, Expression r, AssignOperator op) : Statement(l.Position)
{
	public AssignStatement(ArithmeticExpression ae) 
		: this(ae.Left, ae.Right, AssignOperatorFromArithmeticOperator(ae.Operator))
	{ }

	public AssignStatement(UnaryExpression ue)
		: this(ue.Expr, new IntegerLiteralExpression(ue.Position, 1), AssignOperatorFromUnaryOperator(ue.Operator))
	{ }

	private static AssignOperator AssignOperatorFromArithmeticOperator(ArithmeticOperator op) => op switch {
		ArithmeticOperator.Assign => AssignOperator.Assign,
		ArithmeticOperator.InPlaceAdd => AssignOperator.InPlaceAdd,
		ArithmeticOperator.InPlaceSub => AssignOperator.InPlaceSub,
		ArithmeticOperator.InPlaceMul => AssignOperator.InPlaceMul,
		ArithmeticOperator.InPlaceDiv => AssignOperator.InPlaceDiv,
		ArithmeticOperator.InPlaceIDiv => AssignOperator.InPlaceIDiv,
		_ => throw new NotImplementedException(),
	};

	private static AssignOperator AssignOperatorFromUnaryOperator(UnaryOperator op) => op switch {
		UnaryOperator.Inc => AssignOperator.InPlaceAdd,
		UnaryOperator.Dec => AssignOperator.InPlaceSub,
		_ => throw new NotImplementedException()
	};

	public Expression Left { get; } = l;
	public Expression Right { get; } = r;
	public AssignOperator Op { get; } = op;

	public override NodeType Type => NodeType.AssignStatement;

	public override void Accept(IVisitor visitor) => visitor.VisitAssignStatement(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitAssignStatement(this);
}
