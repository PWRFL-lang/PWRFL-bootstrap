namespace PWR.Compiler.Ast;

public class BaseVisitor : IVisitor
{
	public virtual void Visit(Node? node) => node?.Accept(this);
	public virtual void Visit<T>(T[]? list) where T: Node
	{
		if (list != null) {
			foreach (var node in list) {
				node.Accept(this);
			}
		}
	}

	public virtual void VisitProject(Project node) => Visit(node.Files);

	public virtual void VisitCodeFile(CodeFile node)
	{
		Visit(node.Decls);
		Visit(node.Body);
	}

	public virtual void VisitModuleDeclaration(ModuleDeclaration node)
	{
		Visit(node.Annotations);
		Visit(node.Name);
		Visit(node.Init);
		Visit(node.Body);
	}

	public virtual void VisitAnnotation(Annotation node) => Visit(node.Call);

	public virtual void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		Visit(node.Name);
		Visit(node.Parameters);
		Visit(node.ReturnType);
		Visit(node.Body);
	}

	public virtual void VisitParameterDeclaration(ParameterDeclaration node)
	{
		Visit(node.Name);
		Visit(node.ParamType);
		Visit(node.DefaultValue);
	}

	public virtual void VisitVarDeclaration(VarDeclaration node) => Visit(node.VarType);

	public virtual void VisitSimpleTypeReference(SimpleTypeReference node)
	{ }
	
	public virtual void VisitArrayTypeReference(ArrayTypeReference node)
	{
		Visit(node.BaseType);
		Visit(node.Size);
	}

	public virtual void VisitSpanTypeReference(SpanTypeReference node) => Visit(node.BaseType);

	public virtual void VisitRefTypeReference(RefTypeReference node) => Visit(node.BaseType);

	public virtual void VisitSequenceTypeReference(SequenceTypeReference node) => Visit(node.BaseType);

	public virtual void VisitAssignStatement(AssignStatement node)
	{
		Visit(node.Left);
		Visit(node.Right);
	}

	public virtual void VisitIfStatement(IfStatement node)
	{
		Visit(node.Cond);
		Visit(node.TrueBlock);
		Visit(node.FalseBlock);
	}

	public virtual void VisitForStatement(ForStatement node)
	{
		Visit(node.Index);
		Visit(node.Coll);
		Visit(node.Body);
	}

	public virtual void VisitWhileStatement(WhileStatement node)
	{
		Visit(node.Cond);
		Visit(node.Body);
	}

	public virtual void VisitVarDeclarationStatement(VarDeclarationStatement node)
	{
		Visit(node.Decl);
		Visit(node.Value);
	}

	public virtual void VisitReturnStatement(ReturnStatement node) => Visit(node.Value);

	public virtual void VisitExpressionStatement(ExpressionStatement node) => Visit(node.Expr);

	public virtual void VisitBlock(Block node) => Visit(node.Body);

	public virtual void VisitFunctionCallExpression(FunctionCallExpression node)
	{
		Visit(node.Target);
		Visit(node.Args);
	}

	public virtual void VisitIndexingExpression(IndexingExpression node)
	{
		Visit(node.Expr);
		Visit(node.Indices);
	}

	public virtual void VisitSliceExpression(SliceExpression node)
	{
		Visit(node.Start);
		Visit(node.End);
	}

	public virtual void VisitUnaryExpression(UnaryExpression node) => Visit(node.Expr);

	public virtual void VisitArithmeticExpression(ArithmeticExpression node)
	{
		Visit(node.Left);
		Visit(node.Right);
	}

	public virtual void VisitComparisonExpression(ComparisonExpression node)
	{
		Visit(node.Left);
		Visit(node.Right);
	}

	public virtual void VisitTernaryExpression(TernaryExpression node)
	{
		Visit(node.Cond);
		Visit(node.Left);
		Visit(node.Right);
	}

	public virtual void VisitMatchExpression(MatchExpression node)
	{
		Visit(node.Value);
		Visit(node.Cases);
	}

	public virtual void VisitMatchCaseExpression(MatchCaseExpression node)
	{
		Visit(node.Cases);
		Visit(node.Value);
	}

	public virtual void VisitNewArrayExpression(NewArrayExpression node)
	{
		Visit(node.ArrayType);
		Visit(node.Size);
	}

	public virtual void VisitNewObjExpression(NewObjExpression node)
	{
		Visit(node.ObjectType);
		Visit(node.Args);
	}

	public virtual void VisitCastExpression(CastExpression node)
	{
		Visit(node.Value);
		Visit(node.CastType);
	}

	public virtual void VisitRefExpression(RefExpression node) => Visit(node.Expr);

	public virtual void VisitIdentifier(Identifier node)
	{ }

	public virtual void VisitMemberIdentifier(MemberIdentifier node) => Visit(node.ParentExpr);

	public virtual void VisitCharLiteralExpression(CharLiteralExpression node)
	{ }

	public virtual void VisitStringLiteralExpression(StringLiteralExpression node)
	{ }

	public virtual void VisitIntegerLiteralExpression(IntegerLiteralExpression node)
	{ }

	public virtual void VisitNullLiteralExpression(NullLiteralExpression node)
	{ }
}
