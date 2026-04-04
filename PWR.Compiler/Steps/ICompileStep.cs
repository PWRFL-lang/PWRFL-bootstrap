using PWR.Compiler.Ast;

namespace PWR.Compiler.Steps;

public interface ICompileStep
{
	Project Run(Project tree);
}
