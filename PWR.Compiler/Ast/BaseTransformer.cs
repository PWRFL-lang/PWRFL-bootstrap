using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Steps;

namespace PWR.Compiler.Ast;

public class BaseTransformer : ITransformer
{
	private readonly AssignParents _parents = new();

	public virtual Node? Visit(Node? node) => node?.Accept(this);
	public virtual T[]? Visit<T>(T[]? list) where T : Node
	{
		if (list == null || list.Length == 0) {
			return list;
		}
		var result = new List<T>();
		foreach (var node in list) {
			var value = (T?)node.Accept(this);
			if (value != null) {
				result.Add(value);
			}
		}
		if (result.Count == 0) {
			return null;
		}
		if (result.Count == list.Length && result.SequenceEqual(list)) {
			return list;
		}
		return [..result];
	}

	protected virtual Identifier? VisitID(Identifier? node) => (Identifier?)Visit(node);

	protected virtual Expression? VisitExpression(Expression? node) => (Expression?)Visit(node);

	protected virtual TypeReference? VisitType(TypeReference? node) => (TypeReference?)Visit(node);

	public virtual Node? VisitProject(Project node)
	{
		var files = Visit(node.Files);
		return files == node.Files ? node : node.With(files ?? []);
	}

	public virtual Node? VisitCodeFile(CodeFile node)
	{
		var decls = Visit(node.Decls);
		var body = Visit(node.Body);
		if (decls == node.Decls && body == node.Body) {
			return node;
		}
		var result = node.With(decls ?? [], body ?? []);
		_parents.Visit(result);
		return result;
	}

	public virtual Node? VisitModuleDeclaration(ModuleDeclaration node)
	{
		var annotations = Visit(node.Annotations) ?? [];
		var name = (Identifier)VisitIdentifier(node.Name)!;
		var init = Visit(node.Init)!;
		var body = Visit(node.Body)!;
		if (annotations == node.Annotations && name == node.Name && body == node.Body && init == node.Init) {
			return node;
		}
		return node.With(annotations, name, body, init);
	}

	public virtual Node? VisitAnnotation(Annotation node)
	{
		var result = VisitExpression(node.Call);
		if (result == null) {
			return null;
		}
		return result == node.Call ? node : new Annotation(node.Position, node.Call);
	}

	public virtual Node? VisitFunctionDeclaration(FunctionDeclaration node)
	{
		var annotations = Visit(node.Annotations);
		var name = VisitID(node.Name);
		var parameters = Visit(node.Parameters);
		var ret = VisitType(node.ReturnType);
		var body = Visit(node.Body);

		if (annotations == node.Annotations && name == node.Name && parameters == node.Parameters && ret == node.ReturnType && body == node.Body) {
			return node;
		}
		return node.With(name!, parameters ?? [], ret, body ?? []);
	}

	public virtual Node? VisitParameterDeclaration(ParameterDeclaration node)
	{
		var name = VisitID(node.Name);
		var typ = VisitType(node.ParamType);
		var @default = VisitExpression(node.DefaultValue);
		if (name == node.Name && typ == node.ParamType && @default == node.DefaultValue) {
			return node;
		}
		return new ParameterDeclaration(name!, typ!, @default);
	}

	public virtual Node? VisitVarDeclaration(VarDeclaration node)
	{
		var typ = VisitType(node.VarType);
		if (typ == node.VarType) {
			return node;
		}
		return new VarDeclaration(node.Position, node.Name, typ);
	}

	public virtual Node? VisitSimpleTypeReference(SimpleTypeReference node) => node;

	public virtual Node? VisitArrayTypeReference(ArrayTypeReference node)
	{
		var typ = VisitType(node.BaseType)!;
		var size = VisitExpression(node.Size);
		if (typ == node.BaseType && size == node.Size) {
			return node;
		}
		return new ArrayTypeReference(typ, size);
	}

	public virtual Node? VisitSpanTypeReference(SpanTypeReference node)
	{
		var typ = VisitType(node.BaseType)!;
		return typ == node.BaseType ? node : new SpanTypeReference(typ);
	}

	public virtual Node? VisitRefTypeReference(RefTypeReference node)
	{
		var typ = VisitType(node.BaseType)!;
		return typ == node.BaseType ? node : new RefTypeReference(typ);
	}

	public virtual Node? VisitSequenceTypeReference(SequenceTypeReference node)
	{
		var typ = VisitType(node.BaseType)!;
		return typ == node.BaseType ? node : new SequenceTypeReference(typ);
	}

	public virtual Node? VisitAssignStatement(AssignStatement node)
	{
		var l = VisitExpression(node.Left)!;
		var r = VisitExpression(node.Right)!;
		return (l == node.Left && r == node.Right)
			? node
			: new AssignStatement(l, r, node.Op);
	}

	public virtual Node? VisitIfStatement(IfStatement node)
	{
		var cond = VisitExpression(node.Cond)!;
		var tb = Visit(node.TrueBlock) ?? [];
		var fb = Visit(node.FalseBlock);
		return (cond == node.Cond && tb == node.TrueBlock && fb == node.FalseBlock) 
			? node
			: new IfStatement(node.Position, cond, tb, fb);
	}

	public virtual Node? VisitForStatement(ForStatement node)
	{
		var idx = (VarDeclaration)Visit(node.Index)!;
		var coll = VisitExpression(node.Coll)!;
		var body = Visit(node.Body) ?? [];
		return (idx == node.Index && coll == node.Coll && body == node.Body)
			? node
			: node.With(idx, coll, body);
	}

	public virtual Node? VisitWhileStatement(WhileStatement node)
	{
		var cond = VisitExpression(node.Cond)!;
		var body = Visit(node.Body) ?? [];
		return (cond == node.Cond && body == node.Body)
			? node
			: new WhileStatement(node.Position, cond, body);
	}

	public virtual Node? VisitVarDeclarationStatement(VarDeclarationStatement node)
	{
		var decl = (VarDeclaration)Visit(node.Decl)!;
		var value = VisitExpression(node.Value)!;
		return (decl == node.Decl && value == node.Value)
			? node
			: new VarDeclarationStatement(node.Position, decl, value, node.VarType);
	}

	public virtual Node? VisitReturnStatement(ReturnStatement node)
	{
		var ret = VisitExpression(node.Value);
		return ret == node.Value ? node : new ReturnStatement(node.Position, ret);
	}

	public virtual Node? VisitExpressionStatement(ExpressionStatement node)
	{
		var expr = VisitExpression(node.Expr)!;
		return expr == node.Expr ? node : new ExpressionStatement(expr);
	}
	public virtual Node? VisitBlock(Block node)
	{
		var body = Visit(node.Body);
		return body == node.Body ? node : body == null ? null : node.With(body);
	}

	public virtual Node? VisitFunctionCallExpression(FunctionCallExpression node)
	{
		var target = VisitID(node.Target)!;
		var args = Visit(node.Args) ?? [];
		return (target == node.Target && args == node.Args)
			? node
			: node.With(target, args);
	}

	public virtual Node? VisitIndexingExpression(IndexingExpression node)
	{
		var expr = VisitExpression(node.Expr)!;
		var indices = Visit(node.Indices) ?? [];
		return (expr == node.Expr && indices == node.Indices) ? node : new IndexingExpression(expr, indices);
	}

	public virtual Node? VisitSliceExpression(SliceExpression node)
	{
		var start = VisitExpression(node.Start);
		var end = VisitExpression(node.End);
		return (start == node.Start && end == node.End) ? node : new SliceExpression(node.Position, start, end);
	}

	public virtual Node? VisitUnaryExpression(UnaryExpression node)
	{
		var expr = VisitExpression(node.Expr)!;
		return expr == node.Expr ? node : new UnaryExpression(node.Position, expr, node.Operator);
	}

	public virtual Node? VisitArithmeticExpression(ArithmeticExpression node)
	{
		var l = VisitExpression(node.Left)!;
		var r = VisitExpression(node.Right)!;
		return (l == node.Left && r == node.Right) ? node : new ArithmeticExpression(l, r, node.Operator);
	}

	public virtual Node? VisitComparisonExpression(ComparisonExpression node)
	{
		var l = VisitExpression(node.Left)!;
		var r = VisitExpression(node.Right)!;
		return (l == node.Left && r == node.Right) ? node : new ComparisonExpression(l, r, node.Operator);
	}

	public virtual Node? VisitTernaryExpression(TernaryExpression node)
	{
		var cond = VisitExpression(node.Cond)!;
		var l = VisitExpression(node.Left)!;
		var r = VisitExpression(node.Right)!;
		return (cond == node.Cond && l == node.Left && r == node.Right)
			? node
			: new TernaryExpression(node.Position, cond, l, r);
	}

	public virtual Node? VisitMatchExpression(MatchExpression node)
	{
		var value = VisitExpression(node.Value)!;
		var cases = Visit(node.Cases)!;
		return value == node.Value && cases == node.Cases ? node : new MatchExpression(node.Position, value, cases);
	}

	public virtual Node? VisitMatchCaseExpression(MatchCaseExpression node)
	{
		var cases = Visit(node.Cases);
		var value = VisitExpression(node.Value)!;
		return cases == node.Cases && value == node.Value ? node : new MatchCaseExpression(node.Position, cases, value);
	}

	public virtual Node? VisitNewArrayExpression(NewArrayExpression node)
	{
		var typ = VisitType(node.ArrayType)!;
		var size = VisitExpression(node.Size)!;
		return typ == node.ArrayType && node.Size == size ? node : new NewArrayExpression(node.Position, typ, size);
	}

	public virtual Node? VisitNewObjExpression(NewObjExpression node)
	{
		var typ = VisitType(node.ObjectType)!;
		var args = Visit(node.Args) ?? [];
		return typ == node.ObjectType && args == node.Args ? node : new NewObjExpression(node.Position, typ, args);
	}

	public virtual Node? VisitCastExpression(CastExpression node)
	{
		var value = VisitExpression(node.Value)!;
		var typ = VisitType(node.CastType)!;
		return value == node.Value && typ == node.CastType ? node : new CastExpression(value, typ);
	}

	public virtual Node? VisitRefExpression(RefExpression node)
	{
		var expr = VisitExpression(node.Expr)!;
		return expr == node.Expr ? node : new RefExpression(node.Position, expr);
	}

	public virtual Node? VisitIdentifier(Identifier node) => node;

	public virtual Node? VisitMemberIdentifier(MemberIdentifier node)
	{
		var parent = VisitExpression(node.ParentExpr);
		return parent == null ? null : parent == node.ParentExpr ? node : new MemberIdentifier(parent, node.Name);
	}

	public virtual Node? VisitCharLiteralExpression(CharLiteralExpression node) => node;

	public virtual Node? VisitStringLiteralExpression(StringLiteralExpression node) => node;

	public virtual Node? VisitIntegerLiteralExpression(IntegerLiteralExpression node) => node;

	public virtual Node? VisitNullLiteralExpression(NullLiteralExpression node) => node;
}
