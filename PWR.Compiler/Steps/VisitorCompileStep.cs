using PWR.Compiler.Ast;

namespace PWR.Compiler.Steps;

public class VisitorCompileStep : BaseVisitor, ICompileStep
{
	public virtual Project Run(Project tree)
	{
		Visit(tree);
		return tree;
	}
}
