using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

internal class BindExpressionsP3 : ScopeSensitiveCompileStep
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
			((ISetTypeSemantic)decl.Semantic!).Type = typ;
		} else if (decl.VarType.Semantic!.Type != typ) {
			throw new NotImplementedException();
		}
	}

	public override void VisitAnnotation(Annotation node)
	{ }

	public override void VisitVarDeclarationStatement(VarDeclarationStatement node)
	{
		base.VisitVarDeclarationStatement(node);
		var ancestor = node.Parent;
		while (ancestor is not (null or TypeDeclaration or FunctionDeclaration)) {
			ancestor = ancestor.Parent;
		}
		node.Semantic ??= ancestor is TypeDeclaration ? new Field(node) : node.Decl.Semantic;
		BindDeclType(node.Decl, GetType(node.Value));
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

	public override void VisitVarDeclaration(VarDeclaration node)
	{
		if (node.Semantic == null) {
			Visit(node.VarType);
			var containingScope = _scopes.Peek();
			node.Semantic = containingScope switch {
				FunctionDeclaration or CodeFile or Block => new VariableDecl(node),
				_ => throw new NotImplementedException()
			};
			containingScope.Add(node.Semantic);
		}
	}

	public override void VisitFunctionCallExpression(FunctionCallExpression node)
	{
		base.VisitFunctionCallExpression(node);
		var target = node.Target.Semantic ?? throw new CompileError(node.Target, $"Unable to look up semantic information for '{node.Target}'");
		if (target.SemanticType.HasFlag(SemanticType.Function)) {
			node.Semantic = target;
		} else if (target.SemanticType.HasFlag(SemanticType.Type) && target.Type is ICompositeType ct) {
			node.Semantic = new ImplicitConstructor(ct);
		} else throw new CompileError(node, $"No function named '{node.Target}' could be found.");
		if (node.Semantic is MagicFunction mf) {
			mf.ReturnType.Semantic = mf.FullName switch {
				"ord" => InferOrdType(node),
				"range" => new TypeRef(new SequenceType(MergeTypes(node.Args.Select(GetType)))),
				"$print" => new TypeRef(Types.Int32),
				"StrToPtr" or "span$ToPtr" => new TypeRef(Types.Ptr),
				"ptr$AsSpan" => new TypeRef(mf.Type),
				_ => throw new CompileError(node, $"Unknown magic function name: '{mf.FullName}'"),
			};
		}
		var func = node.Semantic as IFunction ?? throw new CompileError(node, $"Function call is not bound to a function");
		for (int i = 0; i < node.Args.Length; ++i) {
			var paramType = func.Args[i].ParamType.Semantic?.Type;
			if (paramType != null && paramType != node.Args[i].Semantic!.Type) {
				node.Args[i].Annotate("cast", paramType);
			}
		}
	}

	public override void VisitMemberIdentifier(MemberIdentifier node)
	{
		Visit(node.ParentExpr);
		var pSem = node.ParentExpr.Semantic
			?? throw new CompileError(node.ParentExpr, $"No value named '{node.ParentExpr}' could be found.");
		var sem = pSem.Type.GetMember(node.Name);
		if (sem == null) {
			var matches = Scan(s => s.Type is IModule m && m.ExtendsType == pSem.Type && m.GetMember(node.Name) != null, SemanticType.Type);
			if (matches.Count == 0) {
				throw new CompileError(node, $"Type '{pSem.Type}' does not contain a member named '{node.Name}'.");
			}
			sem = matches[0].Type.GetMember(node.Name);
		}
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

	public override void VisitNullLiteralExpression(NullLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.Ptr);

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
			if (idx is SliceExpression) {
				type = type is SpanType ? type : SpanType.Create((type as ICollectionType)?.BaseType ?? throw new CompileError(idx, $"Type '{type}' is not indexable"));
			} else {
				type = (type as ICollectionType)?.BaseType ?? throw new CompileError(idx, $"Type '{type}' is not indexable");
			}
		}
		node.Semantic = new Indexing(node, type);
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
		node.Semantic = new NewArray(node, ArrayType.Create(type));
	}

	public override void VisitNewObjExpression(NewObjExpression node)
	{
		base.VisitNewObjExpression(node);
		var type = GetType(node.ObjectType);
		node.Semantic = new NewObject(node, type);
	}

	public override void VisitRefExpression(RefExpression node)
	{
		base.VisitRefExpression(node);
		node.Semantic = new Ref(node);
	}

	public override void VisitSimpleTypeReference(SimpleTypeReference node) 
		=> node.Semantic ??= new TypeRef(node.Name switch {
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
		if (node.Semantic == null) {
			base.VisitSpanTypeReference(node);
			var baseType = GetType(node.BaseType);
			node.Semantic = new TypeRef(SpanType.Create(baseType));
		}
	}

	public override void VisitRefTypeReference(RefTypeReference node)
	{
		if (node.Semantic == null) {
			base.VisitRefTypeReference(node);
			var baseType = GetType(node.BaseType);
			node.Semantic = new TypeRef(RefType.Create(baseType));
		}
	}
}
