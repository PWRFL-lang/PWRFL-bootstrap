using System;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

public class BindFunctions : ScopeSensitiveCompileStep
{
	private static IType GetType(ISemanticNode node) => node.Semantic?.Type ?? throw new CompileError((Node)node, $"No type bound for node.");

	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		base.VisitFunctionDeclaration(node);
		var containingScope = _scopes.Peek();
		var owner = containingScope is ModuleDeclaration ? ((Declaration)containingScope).Semantic : null;
		node.Semantic = new FunctionDef(node, owner);
		_scopes.Peek().Add(node.Semantic);
	}

	public override void VisitParameterDeclaration(ParameterDeclaration node)
	{
		Visit(node.ParamType);
		var paramCount = ((FunctionDeclaration)_scopes.Peek()).SymbolTable.Count;
		node.Semantic = new ParamDef(node, paramCount);
		_scopes.Peek().Add(node.Semantic);
	}

	public override void VisitSimpleTypeReference(SimpleTypeReference node)
		=> node.Semantic = new TypeRef(node.Name switch {
			"void" => Types.Void,
			"bool" => Types.Bool,
			"char" => Types.Char,
			"int" => Types.Int32,
			"string" => Types.String,
			"ptr" => Types.Ptr,
			_ => throw new NotImplementedException()
		});

	public override void VisitSpanTypeReference(SpanTypeReference node)
	{
		base.VisitSpanTypeReference(node);
		var baseType = GetType(node.BaseType);
		node.Semantic = new TypeRef(baseType.MakeSpan());
	}

	public override void VisitRefTypeReference(RefTypeReference node)
	{
		base.VisitRefTypeReference(node);
		var baseType = GetType(node.BaseType);
		node.Semantic = new TypeRef(RefType.Create(baseType));
	}
}
