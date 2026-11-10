using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.External;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Steps;

internal class BindExpressionsP3 : ScopeSensitiveCompileStep
{
	private IType? _selfType;

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
		} else {
			var declaredType = decl.VarType.Semantic!.Type;
			if (declaredType != typ && !typ.CanAssignTo(declaredType)) {
				throw new CompileError(decl, $"A value of type '{typ}' cannot be assigned to a variable of type '{declaredType}'");
			}
			((ISetTypeSemantic)decl.Semantic!).Type = declaredType;
		}
	}

	public override void VisitModuleDeclaration(ModuleDeclaration node)
	{
		var old = _selfType;
		try {
			_selfType = node.ExtendType?.Semantic?.Type;
			base.VisitModuleDeclaration(node);
		} finally {
			_selfType = old;
		}
	}

	public override void VisitStructDeclaration(StructDeclaration node)
	{
		var old = _selfType;
		try {
			_selfType = node.Semantic?.Type;
			base.VisitStructDeclaration(node);
		} finally {
			_selfType = old;
		}
	}

	public override void VisitClassDeclaration(ClassDeclaration node)
	{
		var old = _selfType;
		try {
			_selfType = node.Semantic?.Type;
			base.VisitClassDeclaration(node);
		} finally {
			_selfType = old;
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
		var enclosingSelf = _selfType;
		if (node.Flags.HasFlag(FunctionFlags.ClassScope) && _selfType is IClassType cls) {
			_selfType = MetaclassType.Create(cls);
		}
		_scopes.Push(node);
		try {
			var seenDefault = false;
			foreach (var p in node.Parameters) {
				if (p.DefaultValue is { } dv) {
					if (!dv.IsLiteral) {
						throw new CompileError(dv, "A default parameter value must be a constant");
					}
					Visit(dv);
					seenDefault = true;
				} else if (seenDefault) {
					throw new CompileError(p, $"A parameter without a default ({p.Name.Name}) cannot follow a defaulted parameter");
				}
			}
			Visit(node.Body);
		} finally {
			_scopes.Pop();
			_selfType = enclosingSelf;
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

	public override void VisitCastExpression(CastExpression node)
	{
		base.VisitCastExpression(node);
		node.Semantic = new TypeRef(node.CastType.Semantic!.Type);
	}

	public override void VisitFunctionCallExpression(FunctionCallExpression node)
	{
		base.VisitFunctionCallExpression(node);
		var target = node.Target.Semantic ?? throw new CompileError(node.Target, $"Unable to look up semantic information for '{node.Target}'");
		var st = target.SemanticType;
		if (st.HasFlag(SemanticType.Function)) {
			node.Semantic = target;
		} else if (st.HasFlag(SemanticType.Type) && target.Type is ICompositeType ct) {
			if (target.Type.IsStruct) {
				node.Semantic = new ImplicitConstructor(ct);
			} else throw new CompileError(node, $"Class '{target.Type.Name}' must be constructed via a named constructor.");
		} else throw new CompileError(node, $"No function named '{node.Target}' could be found.");
		if (node.Semantic is MagicFunction mf) {
			mf.ReturnType.Semantic = mf.FullName switch {
				"ord" => InferOrdType(node),
				"range" => new TypeRef(new SequenceType(MergeTypes(node.Args.Select(GetType)))),
				"$print" => new TypeRef(Types.Int32),
				"StrToPtr" or "span$ToPtr" or "arr$ToPtr" => new TypeRef(Types.Ptr),
				"ptr$AsSpan" or "ref$AsSpan" => new TypeRef(mf.Type),
				"arr$Resize" => new TypeRef(Types.Void),
				_ => throw new CompileError(node, $"Unknown magic function name: '{mf.FullName}'"),
			};
		}
		var func = node.Semantic as IFunction ?? throw new CompileError(node, $"Function call is not bound to a function");
		CheckAbstractAccess(node);
		var selfOffset = func.HasSelf && func.Args is [{ Name.Name: "self" }, ..] ? 1 : 0;
		if (node.Semantic is not (MagicFunction or ImplicitConstructor) && NeedsNormalizedArgs(node, func, selfOffset)) {
			NormalizeArguments(node, func.Args.AsSpan(selfOffset));
		}
		for (int i = 0; i < node.Args.Length; ++i) {
			var paramType = func.Args[i + selfOffset].ParamType.Semantic?.Type;
			var argType = node.Args[i].Semantic!.Type;
			if (paramType != null && paramType != argType) {
				if (IsClassLike(paramType) && IsClassLike(argType) && !argType.CanAssignTo(paramType)) {
					throw new CompileError(node.Args[i], $"A value of type '{argType}' cannot be passed as a parameter of type '{paramType}'.");
				}
				node.Args[i].Annotate("cast", paramType);
			}
		}
	}

	private static void CheckAbstractAccess(FunctionCallExpression node)
	{
		if (node.Target is not MemberIdentifier { ParentExpr: { } receiver } || receiver is SelfLiteralExpression) {
			return;
		}
		if (receiver.Semantic is not { SemanticType: var receiverKind, Type: IClassType cls } || !receiverKind.HasFlag(SemanticType.Type)) {
			return; // not a class name
		}
		if (node.Semantic!.SemanticType.HasFlag(SemanticType.Constructor) && cls.IsAbstract) {
			throw new CompileError(node, $"Cannot construct abstract class '{cls.Name}'.");
		}
		var slot = cls.SlotOf(node.Semantic!.Name);
		if (slot >= 0 && cls.Vmt[slot].IsAbstract) {
			throw new CompileError(node, $"Cannot call abstract method '{cls.Name}.{node.Semantic.Name}'.");
		}
	}

	private static void CheckAbstractConstantRead(MemberIdentifier node)
	{
		if (node.Semantic is not IClassConstant constant || node.ParentExpr is SelfLiteralExpression) {
			return;
		}
		if (node.ParentExpr.Semantic is { SemanticType: var kind, Type: IClassType cls } && kind.HasFlag(SemanticType.Type)) {
			var slot = cls.SlotOf(constant.Name);
			if (slot >= 0 && cls.Vmt[slot].IsAbstract) {
				throw new CompileError(node, $"Cannot read abstract member '{cls.Name}.{constant.Name}'.");
			}
		}
	}

	private static bool IsClassLike(IType type) => type is IClassType or MetaclassType;

	private static bool NeedsNormalizedArgs(FunctionCallExpression node, IFunction func, int selfOffset)
		=> (node.Args.Length != func.Args.Length - selfOffset) || node.Args.Any(a => a.HasAnnotation("argName"));

	private void NormalizeArguments(FunctionCallExpression node, Span<ParameterDeclaration> parameters)
	{
		var result = new Expression[parameters.Length];
		var seenNamed = false;
		var positional = 0;
		foreach (var arg in node.Args) {
			if (arg.GetAnnotation("argName") is string argName) {
				seenNamed = true;
				var idx = -1;
				for (int i = 0; i < parameters.Length; ++i) {
					if (parameters[i].Name.Name == argName) {
						idx = i;
						break;
					}
				}
				if (idx < 0) {
					throw new CompileError(node, $"'{node.Target}' has no parameter named '{argName}'.");
				}
				if (result[idx] != null) {
					throw new CompileError(node, $"Argment for parameter named '{argName}' was given more than once.");
				}
				result[idx] = arg;
			} else {
				if (seenNamed) {
					throw new CompileError(arg, "a positional argument cannot follow a named argument");
				}
				if (positional >= parameters.Length) {
					throw new CompileError(node, $"Too many arguments in call to '{node.Target}'");
				}
				result[positional] = arg;
				++positional;
			}
		}
		for (int i = 0; i < parameters.Length; ++i) {
			result[i] ??= CloneLiteral(parameters[i].DefaultValue
				?? throw new CompileError(node, $"No argument given for required parameter '{parameters[i].Name.Name}'"));
		}
		node.Args = result;
	}

	internal static LiteralExpression CloneLiteral(Expression e) => e switch {
		IntegerLiteralExpression il => new IntegerLiteralExpression(il.Position, il.Value) { Semantic = e.Semantic },
		CharLiteralExpression cl => new CharLiteralExpression(cl.Position, cl.Value) { Semantic = e.Semantic },
		StringLiteralExpression sl => new StringLiteralExpression(sl.Position, sl.Value) { Semantic = e.Semantic },
		BooleanLiteralExpression bl => new BooleanLiteralExpression(bl.Position, bl.Value) { Semantic = e.Semantic },
		NilLiteralExpression nl => new NilLiteralExpression(nl.Position) { Semantic = e.Semantic },
		_ => throw new CompileError(e, "A default parameter value must be a constant")
	};

	public override void VisitMemberIdentifier(MemberIdentifier node)
	{
		Visit(node.ParentExpr);
		var pSem = node.ParentExpr.Semantic
			?? throw new CompileError(node.ParentExpr, $"No value named '{node.ParentExpr}' could be found.");
		var semType = pSem.Type;
		while (semType != null) {
			var sem = semType.GetMember(node.Name);
			if (sem == null) {
				var matches = Scan(s => s.Type is IModule m && m.ExtendsType == semType && m.GetMember(node.Name) != null, SemanticType.Type);
				if (matches.Count == 0) {
					semType = UnwrapType(semType, node);
					continue;
				}
				sem = matches[0].Type.GetMember(node.Name);
			}
			node.Semantic = sem;
			CheckAbstractConstantRead(node);
			return;
		}
		throw new CompileError(node, $"Type '{pSem.Type}' does not contain a member named '{node.Name}'.");
	}

	private static IType? UnwrapType(IType type, MemberIdentifier node)
	{
		if (type is RefType rt) {
			return rt.BaseType;
		}
		if (type is NilableType nt /*&& ProvableNotNil(node)*/) {
			return nt.BaseType;
		}
		return null;
	}

	/*
	 // This is harder than it seems at first glance.  Work on this later.
	private static bool ProvableNotNil(MemberIdentifier node)
	{
		Node current = node;
		while (current is not (CodeFile or FunctionDeclaration)) {
			current = current.Parent;
			if (current is IConditional ic) {
				throw new NotImplementedException();
			}
		}
		return false;
	}
	*/

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
		if (node.Left.Semantic is IClassConstant constant) {
			throw new CompileError(node, $"'{constant.Name}' is a constant, and cannot be assigned to.");
		}
		var lType = GetType(node.Left);
		var rType = GetType(node.Right);
		if (lType != rType) {
			if (!Types.IsCompatible(lType, rType)) {
				throw new CompileError(node, $"A value of type '{rType}' cannot be assigned to a variable of type '{lType}'");
			}
			node.Right.Annotate("cast", lType);
		}
	}

	public override void VisitFieldDeclaration(FieldDeclaration node)
	{
		base.VisitFieldDeclaration(node);
		if (node.Semantic is ClassConstantDecl constant) {
			// The value is already known to be a literal (see BindMembers); it has to suit the type.
			if (node.Value != null) {
				var declared = constant.Type;
				var actual = GetType(node.Value);
				if (declared != actual && !Types.IsCompatible(declared, actual)) {
					throw new CompileError(node, $"A value of type '{actual}' cannot be the value of the constant '{constant.Name}: {declared}'.");
				}
			}
			return;
		}
		if (node.Value == null || node.Semantic is not FieldDecl field) {
			return;
		}
		if (field.ParentType is StructDeclaration) {
			throw new CompileError(node, "Default values for struct fields are not currently supported.");
		}
		var lType = field.Type;
		var rType = GetType(node.Value);
		if (lType != rType) {
			if (!Types.IsCompatible(lType, rType)) {
				throw new CompileError(node, $"A value of type '{rType}' cannot be used as the default for a field of type '{lType}'");
			}
			node.Value.Annotate("cast", lType);
		}
	}

	public override void VisitCharLiteralExpression(CharLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.Char);

	public override void VisitStringLiteralExpression(StringLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.String);

	public override void VisitIntegerLiteralExpression(IntegerLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.Int32);

	public override void VisitBooleanLiteralExpression(BooleanLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.Bool);

	public override void VisitSelfLiteralExpression(SelfLiteralExpression node)
		=> node.Semantic = new SelfRef(node, _selfType ?? throw new CompileError(node, "No 'self' context is available"));

	public override void VisitNilLiteralExpression(NilLiteralExpression node)
		=> node.Semantic = new Literal(node, Types.Nil);

	public override void VisitSuperCallExpression(SuperCallExpression node)
	{
		base.VisitSuperCallExpression(node);
		var cls = _selfType switch {
			InternalClass c => c,
			MetaclassType { Class: InternalClass c } => c,
			_ => throw new CompileError(node, "'super' can only be used inside a class"),
		};
		var member = cls.BaseType.GetMember(node.Name.Name)
			?? throw new CompileError(node, $"'{cls.BaseType.Name}' has no constructor named '{node.Name.Name}'");
		if (member is IFunction { Flags: var flags } && flags.HasFlag(FunctionFlags.Abstract)) {
			throw new CompileError(node, $"Cannot call '{node.Name.Name}' through 'super': it is abstract in '{cls.BaseType.Name}'.");
		}
		node.Semantic = member;
	}

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
		var lType = GetType(node.Left);
		var rType = GetType(node.Right);
		if (lType != rType) {
			node.Right.Annotate("cast", lType);
		}
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
				type = type is SpanType ? type : SpanType.Create((type as ICollectionType)?.ElementType ?? throw new CompileError(idx, $"Type '{type}' is not indexable"));
			} else {
				type = (type as ICollectionType)?.ElementType ?? throw new CompileError(idx, $"Type '{type}' is not indexable");
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
			_ => CheckType(LookupType(node.Name), node)
		});

	private static IType CheckType(List<ISemantic> list, SimpleTypeReference node) => list.Count switch {
		0 => throw new CompileError(node, $"No type named '{node.Name}' could be found."),
		1 => list[0].Type,
		_ => throw new CompileError(node, $"Multiple types named '{node.Name}' found in scope."),
	};

	public override void VisitMetaTypeReference(MetaTypeReference node)
	{
		if (node.Semantic == null) {
			base.VisitMetaTypeReference(node);
			node.Semantic = new TypeRef(node.BaseType == null ? MetaclassType.Any : MetaclassType.For(GetType(node.BaseType), node.BaseType));
		}
	}

	public override void VisitMetaclassExpression(MetaclassExpression node)
	{
		base.VisitMetaclassExpression(node);
		var type = MetaclassType.For(GetType(node.Type), node.Type);
		if (type.Class.IsAbstract) {
			throw new CompileError(node, $"Cannot take the metaclass of abstract class '{type.Class.Name}'.");
		}
		node.Semantic = new MetaclassLiteral(node, type);
	}

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
