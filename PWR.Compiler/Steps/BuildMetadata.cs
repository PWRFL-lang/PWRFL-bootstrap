using PWR.Compiler.Ast;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

internal class BuildMetadata(string name) : ScopeSensitiveCompileStep
{
	private readonly MetadataContext _context = new();

	public override void VisitProject(Project node)
	{
		_context.AddLibrary(name);
		base.VisitProject(node);
		(node.MetadataHeader, node.Metadata, node.BlobData) = _context.Serialize();
	}

	public override void VisitModuleDeclaration(ModuleDeclaration node)
	{
		var parent = _scopes.Peek();
		_context.AddType(node.Semantic!, (parent as ISemanticNode)?.Semantic);
		base.VisitModuleDeclaration(node);
	}

	public override void VisitVarDeclarationStatement(VarDeclarationStatement node)
	{
		var parent = _scopes.Peek();
		if (node.Semantic!.SemanticType.HasFlag(SemanticType.Field)) {
			_context.AddField(node.Semantic, (parent as ISemanticNode)?.Semantic);
		}
		base.VisitVarDeclarationStatement(node);
	}

	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		var parent = _scopes.Peek();
		_context.AddFunction((IFunction)node.Semantic!, (parent as ISemanticNode)?.Semantic);
		base.VisitFunctionDeclaration(node);
	}

	public override void VisitParameterDeclaration(ParameterDeclaration node)
	{
		var parent = _scopes.Peek();
		_context.AddParam((ParamDef)node.Semantic!, (parent as ISemanticNode)?.Semantic);
		base.VisitParameterDeclaration(node);
	}
}
