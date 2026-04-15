namespace PWR.Compiler.Ast;

public interface IVisitor
{
	void VisitProject(Project node);
	void VisitCodeFile(CodeFile node);
	void VisitAnnotation(Annotation node);
	void VisitModuleDeclaration(ModuleDeclaration node);
	void VisitStructDeclaration(StructDeclaration node);
	void VisitFieldDeclaration(FieldDeclaration node);
	void VisitFunctionDeclaration(FunctionDeclaration node);
	void VisitParameterDeclaration(ParameterDeclaration node);
	void VisitVarDeclaration(VarDeclaration node);
	void VisitSimpleTypeReference(SimpleTypeReference node);
	void VisitArrayTypeReference(ArrayTypeReference node);
	void VisitSpanTypeReference(SpanTypeReference node);
	void VisitRefTypeReference(RefTypeReference node);
	void VisitSequenceTypeReference(SequenceTypeReference node);
	void VisitAssignStatement(AssignStatement node);
	void VisitIfStatement(IfStatement node);
	void VisitForStatement(ForStatement node);
	void VisitWhileStatement(WhileStatement node);
	void VisitVarDeclarationStatement(VarDeclarationStatement node);
	void VisitReturnStatement(ReturnStatement node);
	void VisitExpressionStatement(ExpressionStatement node);
	void VisitBlock(Block node);
	void VisitFunctionCallExpression(FunctionCallExpression node);
	void VisitIndexingExpression(IndexingExpression node);
	void VisitSliceExpression(SliceExpression node);
	void VisitUnaryExpression(UnaryExpression node);
	void VisitArithmeticExpression(ArithmeticExpression node);
	void VisitComparisonExpression(ComparisonExpression node);
	void VisitTernaryExpression(TernaryExpression node);
	void VisitMatchExpression(MatchExpression node);
	void VisitMatchCaseExpression(MatchCaseExpression node);
	void VisitNewArrayExpression(NewArrayExpression node);
	void VisitNewObjExpression(NewObjExpression node);
	void VisitCastExpression(CastExpression node);
	void VisitRefExpression(RefExpression node);
	void VisitIdentifier(Identifier node);
	void VisitMemberIdentifier(MemberIdentifier node);
	void VisitCharLiteralExpression(CharLiteralExpression node);
	void VisitStringLiteralExpression(StringLiteralExpression node);
	void VisitIntegerLiteralExpression(IntegerLiteralExpression node);
	void VisitNullLiteralExpression(NullLiteralExpression node);
}
