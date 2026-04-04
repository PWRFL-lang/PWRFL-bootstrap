using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

public class BindTypes : ScopeSensitiveCompileStep
{
	public override void VisitModuleDeclaration(ModuleDeclaration node)
	{
		base.VisitModuleDeclaration(node);
		node.Semantic = new Module(node);
		_scopes.Peek().Add(node.Semantic);
	}
}
