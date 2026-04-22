namespace PWR.Compiler.Ast;

public class SliceExpression(Position pos, Expression? start, Expression? end) : Expression(pos)
{
	public Expression? Start { get; } = start;
	public Expression? End { get; } = end;

	public override NodeType Type => NodeType.SliceExpression;

	public override void Accept(IVisitor visitor) => visitor.VisitSliceExpression(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitSliceExpression(this);

	public override string ToString()
	{
		if (Start == null) {
			return $"..{End}";
		}
		if (End == null) {
			return $"{Start}..";
		}
		return $"{Start}..{End}";
	}
}
