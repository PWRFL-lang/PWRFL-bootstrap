using System;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

internal class BindExpressions : ScopeSensitiveCompileStep
{
	public override void VisitAnnotation(Annotation node)
	{ }

	public override void VisitVarDeclarationStatement(VarDeclarationStatement node)
	{
		base.VisitVarDeclarationStatement(node);
		var ancestor = node.Parent;
		while (ancestor is not (null or TypeDeclaration or FunctionDeclaration)) {
			ancestor = ancestor.Parent;
		}
		node.Semantic = ancestor is TypeDeclaration ? new Field(node) : node.Decl.Semantic;
	}

	// for declarations, skip names and only look at expressions
	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		_scopes.Push(node);
		try {
			Visit(node.Body);
		} finally {
			_scopes.Pop();
		}
	}

	public override void VisitFieldDeclaration(FieldDeclaration node)
	{
		if (node.Semantic == null) {
			Visit(node.Decl);
			var containingScope = _scopes.Peek();
			node.Semantic = containingScope switch {
				ModuleDeclaration md => new GlobalFieldDecl(node, md),
				_ => throw new NotImplementedException()
			};
			containingScope.Add(node.Semantic);
		}
	}

	public override void VisitVarDeclaration(VarDeclaration node)
	{
		Visit(node.VarType);
		var containingScope = _scopes.Peek();
		node.Semantic = containingScope switch {
			FunctionDeclaration or CodeFile or Block => new VariableDecl(node),
			_ => throw new NotImplementedException()
		};
		containingScope.Add(node.Semantic);
	}

	public override void VisitFunctionCallExpression(FunctionCallExpression node)
	{
		base.VisitFunctionCallExpression(node);
		var target = node.Target.Semantic ?? throw new CompileError(node.Target, $"Unable to look up semantic information for '{node.Target}'");
		if (!target.SemanticType.HasFlag(SemanticType.Function)) {
			throw new CompileError(node, $"No function named '{node.Target}' could be found.");
		}
		node.Semantic = target;
	}

	public override void VisitMemberIdentifier(MemberIdentifier node)
	{
		Visit(node.ParentExpr);
		var pSem = node.ParentExpr.Semantic
			?? throw new CompileError(node.ParentExpr, $"No value named '{node.ParentExpr}' could be found.");
		var sem = pSem.Type.GetMember(node.Name)
			?? throw new CompileError(node, $"'{node.ParentExpr}' does not contain a member named '{node.Name}'.");
		node.Semantic = sem;
	}

	public override void VisitIdentifier(Identifier node)
	{
		var target = Lookup(node.Name);
		node.Semantic = target.Count switch {
			0 => throw new CompileError(node, $"No value named '{node.Name}' could be found."),
			1 => target[0],
			_ => throw new CompileError(node, $"Multiple elements named '{node.Name}' found in scope."),
		};
	}
}
