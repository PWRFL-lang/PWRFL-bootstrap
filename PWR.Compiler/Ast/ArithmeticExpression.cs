using System.Diagnostics;

namespace PWR.Compiler.Ast;

public enum ArithmeticOperator
{
	Assign,
	Add,
	Subtract,
	Multiply,
	IDivide,
	Divide,
	Modulus,

	InPlaceAdd,
	InPlaceSub,
	InPlaceMul,
	InPlaceDiv,
	InPlaceIDiv,
}

public class ArithmeticExpression(Expression l, Expression r, ArithmeticOperator op) : Expression(l.Position), ITwoBranchExpression
{
	public Expression Left { get; } = l;
	public Expression Right { get; } = r;
	public ArithmeticOperator Operator { get; } = op;

	public override NodeType Type => NodeType.ArithmeticExpression;

	public bool IsAssign => Operator == ArithmeticOperator.Assign || Operator >= ArithmeticOperator.InPlaceAdd;

	public override void Accept(IVisitor visitor) => visitor.VisitArithmeticExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitArithmeticExpression(this);

	public override string ToString()
	{
		var oper = Operator switch {
			ArithmeticOperator.Assign => "=",
			ArithmeticOperator.Add => "+",
			ArithmeticOperator.Subtract => "-",
			ArithmeticOperator.Multiply => "*",
			ArithmeticOperator.IDivide => "//",
			ArithmeticOperator.Divide => "/",
			ArithmeticOperator.Modulus => "%",
			ArithmeticOperator.InPlaceAdd => "+=",
			ArithmeticOperator.InPlaceSub => "-=",
			ArithmeticOperator.InPlaceMul => "*=",
			ArithmeticOperator.InPlaceDiv => "/=",
			ArithmeticOperator.InPlaceIDiv => "//=",
			_ => throw new UnreachableException()
		};
		return $"({Left} {oper} {Right})";
	}
}

