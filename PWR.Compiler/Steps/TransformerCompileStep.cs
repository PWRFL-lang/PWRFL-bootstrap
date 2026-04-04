using PWR.Compiler.Ast;

namespace PWR.Compiler.Steps;

public class TransformerCompileStep : BaseTransformer, ICompileStep
{
	public Project Run(Project tree) => (Project)VisitProject(tree)!;
}
