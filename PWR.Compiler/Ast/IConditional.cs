namespace PWR.Compiler.Ast;

public interface IConditional
{
	Expression Cond { get; }
	Node True { get; }
	Node? False { get; }
}
