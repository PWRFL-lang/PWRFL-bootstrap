namespace PWR.Compiler.Ast;

public interface ITwoBranchExpression
{
	Expression Left { get; }
	Expression Right { get; }

	Expression GetOther(Expression value)
	{
		if (value == Left) {
			return Right;
		}
		if (value == Right) {
			return Left;
		}
		throw new CompileError(value, $"Internal error: Value is not a child of two-branch expression");
	}
}
