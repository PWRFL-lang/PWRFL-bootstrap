using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

public class BindFunctions : ScopeSensitiveCompileStep
{
	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		base.VisitFunctionDeclaration(node);
		node.Semantic = new FunctionDef(node);
		_scopes.Peek().Add(node.Semantic);
	}

	public override void VisitParameterDeclaration(ParameterDeclaration node)
	{
		var paramCount = ((FunctionDeclaration)_scopes.Peek()).SymbolTable.Count;
		node.Semantic = new ParamDef(node, paramCount);
		_scopes.Peek().Add(node.Semantic);
	}
}
