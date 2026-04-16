using System;
using System.Collections.Generic;
using System.Diagnostics;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

public class BindMembers : ScopeSensitiveCompileStep
{
	private static IType GetType(ISemanticNode node) => node.Semantic?.Type ?? throw new CompileError((Node)node, $"No type bound for node.");

	public override void VisitCodeFile(CodeFile node)
	{
		_scopes.Push(node);
		try {
			Visit(node.Decls);
		} finally {
			_scopes.Pop(); 
		}
	}

	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		// do not recurse into function bodies
		_scopes.Push(node);
		try {
			Visit(node.Parameters);
			Visit(node.ReturnType);
		} finally {
			_scopes.Pop(); 
		}
		var containingScope = _scopes.Peek();
		var owner = containingScope is TypeDeclaration ? ((Declaration)containingScope).Semantic : null;
		var hasSelf = containingScope is StructDeclaration
			|| (containingScope is ModuleDeclaration { ExtendType: { } } && node.Parameters is [{ Name.Name: "self" }, ..]);
		node.Semantic = owner == null ? new FunctionDef(node, hasSelf, owner) : new MethodDef(node, hasSelf, owner);
		_scopes.Peek().Add(node.Semantic);
	}

	public override void VisitParameterDeclaration(ParameterDeclaration node)
	{
		Visit(node.ParamType);
		var paramCount = ((FunctionDeclaration)_scopes.Peek()).SymbolTable.Count;
		node.Semantic = new ParamDef(node, paramCount);
		_scopes.Peek().Add(node.Semantic);
	}

	public override void VisitFieldDeclaration(FieldDeclaration node)
	{
		Debug.Assert(node.Semantic == null);
		Visit(node.Decl);
		var containingScope = _scopes.Peek();
		node.Semantic = containingScope switch {
			ModuleDeclaration md => new GlobalFieldDecl(node, md),
			StructDeclaration sd => new FieldDecl(node, sd, sd.FieldCount),
			_ => throw new NotImplementedException()
		};
		containingScope.Add(node.Semantic);
		node.Decl.Semantic = node.Semantic;
	}

	public override void VisitSimpleTypeReference(SimpleTypeReference node)
		=> node.Semantic = new TypeRef(node.Name switch {
			"void" => Types.Void,
			"bool" => Types.Bool,
			"char" => Types.Char,
			"int" => Types.Int32,
			"string" => Types.String,
			"ptr" => Types.Ptr,
			_ => CheckType(Lookup(node.Name, SemanticType.Type), node)
		});

	private static IType CheckType(List<ISemantic> list, SimpleTypeReference node) => list.Count switch {
		0 => throw new CompileError(node, $"No type named '{node.Name}' could be found."),
		1 => list[0].Type,
		_ => throw new CompileError(node, $"Multiple types named '{node.Name}' found in scope."),
	};

	public override void VisitSpanTypeReference(SpanTypeReference node)
	{
		base.VisitSpanTypeReference(node);
		var baseType = GetType(node.BaseType);
		node.Semantic = new TypeRef(SpanType.Create(baseType));
	}

	public override void VisitRefTypeReference(RefTypeReference node)
	{
		base.VisitRefTypeReference(node);
		var baseType = GetType(node.BaseType);
		node.Semantic = new TypeRef(RefType.Create(baseType));
	}
}
