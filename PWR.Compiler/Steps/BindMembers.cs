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

	internal void Bind(FunctionDeclaration function, ClassDeclaration owner, CodeFile file, Project project)
	{
		_scopes.Push(project);
		_scopes.Push(file);
		_scopes.Push(owner);
		try {
			VisitFunctionDeclaration(function);
		} finally {
			_scopes.Pop();
			_scopes.Pop();
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
		if (node.Flags.HasFlag(FunctionFlags.ClassScope) && containingScope is not ClassDeclaration) {
			throw new CompileError(node, "Only a method of a class can be declared 'class'.");
		}
		if (node.Flags.HasFlag(FunctionFlags.Abstract) && containingScope is ClassDeclaration { IsAbstract: false } declaringClass) {
			throw new CompileError(node, $"'{node.Name.Name}' is abstract, but class '{declaringClass.Name}' is not declared abstract.");
		}
		var hasSelf = containingScope is StructDeclaration
			|| containingScope is ClassDeclaration && !node.Flags.HasFlag(FunctionFlags.Static)
			|| (containingScope is ModuleDeclaration { ExtendType: { } } && node.Parameters is [{ Name.Name: "self" }, ..]);
		node.Semantic = owner == null 
			? new FunctionDef(node, hasSelf, owner) 
			: node.IsConstructor 
				? new Constructor(node, owner)
				: new MethodDef(node, hasSelf, owner);
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
		node.Semantic = node.Flags.HasFlag(FunctionFlags.ClassScope)
			? BindClassConstant(node, containingScope)
			: containingScope switch {
				ModuleDeclaration md => new GlobalFieldDecl(node, md),
				StructDeclaration sd => new FieldDecl(node, sd, sd.FieldCount),
				ClassDeclaration cd => new FieldDecl(node, cd, cd.FieldCount),
				_ => throw new NotImplementedException()
			};
		containingScope.Add(node.Semantic);
		node.Decl.Semantic = node.Semantic;
	}

	private static ClassConstantDecl BindClassConstant(FieldDeclaration node, IScope scope)
	{
		var name = node.Decl.Name;
		if (scope is not ClassDeclaration cd) {
			throw new CompileError(node, "Only a class can declare a 'const class' member.");
		}
		if (node.Flags.HasFlag(FunctionFlags.Abstract)) {
			if (!cd.IsAbstract) {
				throw new CompileError(node, $"'{name}' is abstract, but class '{cd.Name}' is not declared abstract.");
			}
			if (node.Value != null) {
				throw new CompileError(node, $"Abstract member '{name}' can't have a value.");
			}
		} else if (node.Value == null) {
			throw new CompileError(node, $"'{name}' needs a value: const class {name}: T = value");
		} else if (!node.Value.IsLiteral) {
			throw new CompileError(node.Value, $"The value of the class constant '{name}' must be constant.");
		}
		if (!node.Flags.HasFlag(FunctionFlags.Override) && node.Decl.VarType == null) {
			throw new CompileError(node, $"'{name}' needs a type: const class {name}: T = value");
		}
		return new ClassConstantDecl(node, cd);
	}

	public override void VisitSimpleTypeReference(SimpleTypeReference node)
		=> node.Semantic ??= new TypeRef(node.Name switch {
			"void" => Types.Void,
			"bool" => Types.Bool,
			"byte" => Types.Byte,
			"char" => Types.Char,
			"int" => Types.Int32,
			"long" => Types.Int64,
			"string" => Types.String,
			"ptr" => Types.Ptr,
			_ => CheckType(LookupType(node.Name), node)
		});

	private static IType CheckType(List<ISemantic> list, SimpleTypeReference node) => list.Count switch {
		0 => throw new CompileError(node, $"No type named '{node.Name}' could be found."),
		1 => list[0].Type,
		_ => throw new CompileError(node, $"Multiple types named '{node.Name}' found in scope."),
	};

	public override void VisitNilableTypeReference(NilableTypeReference node)
	{
		base.VisitNilableTypeReference(node);
		var baseType = GetType(node.BaseType);
		node.Semantic = new TypeRef(NilableType.Create(baseType));
	}

	public override void VisitMetaTypeReference(MetaTypeReference node)
	{
		base.VisitMetaTypeReference(node);
		node.Semantic = new TypeRef(node.BaseType == null ? MetaclassType.Any : MetaclassType.For(GetType(node.BaseType), node.BaseType));
	}

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

	public override void VisitArrayTypeReference(ArrayTypeReference node)
	{
		base.VisitArrayTypeReference(node);
		var baseType = GetType(node.BaseType);
		node.Semantic = new TypeRef(node.Size == null ? ArrayType.Create(baseType) : new InlineArrayType(baseType, node.Size));
	}
}
