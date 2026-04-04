namespace PWR.Compiler.Ast;

public abstract class LiteralExpression(Position position) : Expression(position)
{
	public abstract object LiteralValue { get; }
	public override bool IsLiteral => true;
}
