using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

public class InferTypesP3 : VisitorCompileStep
{
	public override void Visit(Node? node)
	{
		base.Visit(node);
		if (node is ISemanticNode { Semantic: null } && node is not Identifier { Parent: Declaration }) {
			throw new NotImplementedException();
		}
	}

	public override void Visit<T>(T[]? list)
	{
		if (list != null) {
			foreach (var node in list) {
				if (node != null) {
					Visit(node);
				}
			}
		}
	}

	private static IType GetType(ISemanticNode node) => node.Semantic?.Type ?? throw new CompileError((Node)node, $"No type bound for node.");

	private static IType MergeTypes(IType lType, IType rType)
	{
		if (lType == rType) {
			return lType;
		}
		if (Types.IsCompatible(lType, rType)) {
			return lType;
		}
		throw new NotImplementedException();
	}

	private static IType MergeTypes(IEnumerable<IType> types)
	{
		var arr = types.ToArray();
		var typ = arr[0];
		if (arr.Skip(1).Any(t => t != typ)) {
			typ = arr.Aggregate(typ, MergeTypes);
		}
		return typ;
	}

	private static void BindDeclType(VarDeclaration decl, IType typ)
	{
		if (decl.VarType == null) {
			((VariableDecl)decl.Semantic!).Type = typ;
		} else if (decl.VarType.Semantic!.Type != typ) {
			throw new NotImplementedException();
		}
	}

	public override void VisitAnnotation(Annotation node)
	{ }

	public override void VisitVarDeclarationStatement(VarDeclarationStatement node)
	{
		base.VisitVarDeclarationStatement(node);
		BindDeclType(node.Decl, GetType(node.Value));
	}

	public override void VisitForStatement(ForStatement node)
	{
		base.VisitForStatement(node);
		var eType = GetType(node.Coll).ElementType;
		if (eType == null) {
			throw new CompileError(node.Coll, $"Type '{GetType(node.Coll)}' is not iterable");
		} else {
			BindDeclType(node.Index, eType);
		}
	}

	public override void VisitAssignStatement(AssignStatement node)
	{
		base.VisitAssignStatement(node);
		if (GetType(node.Left) != GetType(node.Right)) {
			throw new NotImplementedException();
		}
	}

	public override void VisitCharLiteralExpression(CharLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.Char);

	public override void VisitStringLiteralExpression(StringLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.String);

	public override void VisitIntegerLiteralExpression(IntegerLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.Int32);

	public override void VisitUnaryExpression(UnaryExpression node)
	{
		base.VisitUnaryExpression(node);
		node.Semantic = new Unary(node);
	}

	public override void VisitArithmeticExpression(ArithmeticExpression node)
	{
		base.VisitArithmeticExpression(node);
		var lType = GetType(node.Left);
		var rType = GetType(node.Right);
		IType type;
		if (lType == rType) {
			type = lType;
		} else {
			type = MergeTypes(lType, rType);
			if (lType != type) {
				node.Left.Annotate("cast", type);
			}
			if (rType != type) {
				node.Right.Annotate("cast", type);
			}
		}
		node.Semantic = new Arithmetic(node, type);
	}

	public override void VisitComparisonExpression(ComparisonExpression node)
	{
		base.VisitComparisonExpression(node);
		node.Semantic = new Comparison(node);
	}

	public override void VisitTernaryExpression(TernaryExpression node)
	{
		base.VisitTernaryExpression(node);
		var lType = GetType(node.Left);
		var rType = GetType(node.Right);
		var type = lType == rType ? lType : MergeTypes(lType, rType);
		node.Semantic = new Ternary(node, type);
	}

	public override void VisitIndexingExpression(IndexingExpression node)
	{
		base.VisitIndexingExpression(node);
		var type = GetType(node.Expr);
		foreach (var idx in node.Indices) {
			type = (type as ICollectionType)?.BaseType ?? throw new CompileError(idx, $"Type '{type}' is not indexable");
		}
		node.Semantic = new Indexing(node, type);
	}

	public override void VisitFunctionCallExpression(FunctionCallExpression node)
	{
		base.VisitFunctionCallExpression(node);
		if (node.Semantic is MagicFunction mf) {
			switch (mf.Name) {
				case "ord":
					mf.ReturnType.Semantic = InferOrdType(node);
					break;
				case "range":
					mf.ReturnType.Semantic = new TypeRef(new SequenceType(MergeTypes(node.Args.Select(GetType))));
					break;
				case "print":
					mf.ReturnType.Semantic = new TypeRef(Types.Int32);
					break;
				case "StrToPtr":
					mf.ReturnType.Semantic = new TypeRef(Types.Ptr);
					break;
				default:
					throw new CompileError(node, $"Unknown magic function name: '{mf.Name}'");
			}
		}
	}

	private static TypeRef InferOrdType(FunctionCallExpression node)
	{
		if (node.Parent is TernaryExpression t && t.Cond == node) {
			// this is invalid and will be caught later, but right now don't let it error out in this function
			return new TypeRef(Types.Int32);
		}
		if (node.Parent is ITwoBranchExpression tb) {
			return new TypeRef(GetType(tb.GetOther(node)));
		}
		if (node.Parent is FunctionCallExpression or not Expression) {
			return new TypeRef(Types.Int32);
		}
		throw new NotImplementedException();
	}

	public override void VisitMatchExpression(MatchExpression node)
	{
		base.VisitMatchExpression(node);
		var typ = MergeTypes(node.Cases.Select(c => GetType(c)));
		node.Semantic = new Match(node, typ);
	}

	public override void VisitMatchCaseExpression(MatchCaseExpression node)
	{
		base.VisitMatchCaseExpression(node);
		node.Semantic = node.Value.Semantic;
	}

	public override void VisitSliceExpression(SliceExpression node)
	{
		base.VisitSliceExpression(node);
		node.Semantic = new Slice(node);
	}

	public override void VisitNewArrayExpression(NewArrayExpression node)
	{
		base.VisitNewArrayExpression(node);
		var type = GetType(node.ArrayType);
		node.Semantic = new NewArray(node, type.MakeArray());
	}

	public override void VisitNewObjExpression(NewObjExpression node)
	{
		base.VisitNewObjExpression(node);
		var type = GetType(node.ObjectType);
		node.Semantic = new NewObject(node, type);
	}
}
