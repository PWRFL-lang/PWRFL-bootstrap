using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

public class BindTypes : ScopeSensitiveCompileStep
{
	private string? _namespace;

	public override void VisitCodeFile(CodeFile node)
	{
		_namespace = node.Namespace;
		base.VisitCodeFile(node);
	}

	public override void VisitModuleDeclaration(ModuleDeclaration node)
	{
		base.VisitModuleDeclaration(node);
		node.Semantic = new Module(node) { Namespace = _namespace };
		_scopes.Peek().Add(node.Semantic);
	}

	public override void VisitStructDeclaration(StructDeclaration node)
	{
		base.VisitStructDeclaration(node);
		node.Semantic = new StructDecl(node) { Namespace = _namespace };
		_scopes.Peek().Add(node.Semantic);
	}

	public override void VisitClassDeclaration(ClassDeclaration node)
	{
		base.VisitClassDeclaration(node);
		node.Semantic = new ClassDecl(node) { Namespace = _namespace };
		_scopes.Peek().Add(node.Semantic);
	}
}
