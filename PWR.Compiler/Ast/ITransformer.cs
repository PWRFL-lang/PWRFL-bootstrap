namespace PWR.Compiler.Ast;

public interface ITransformer
{
	Node? VisitProject(Project node);
	Node? VisitCodeFile(CodeFile node);
	Node? VisitAnnotation(Annotation node);
	Node? VisitModuleDeclaration(ModuleDeclaration node);
	Node? VisitFunctionDeclaration(FunctionDeclaration node);
	Node? VisitParameterDeclaration(ParameterDeclaration node);
	Node? VisitVarDeclaration(VarDeclaration node);
	Node? VisitSimpleTypeReference(SimpleTypeReference node);
	Node? VisitArrayTypeReference(ArrayTypeReference node);
	Node? VisitSpanTypeReference(SpanTypeReference node);
	Node? VisitRefTypeReference(RefTypeReference node);
	Node? VisitSequenceTypeReference(SequenceTypeReference node);
	Node? VisitAssignStatement(AssignStatement node);
	Node? VisitIfStatement(IfStatement node);
	Node? VisitForStatement(ForStatement node);
	Node? VisitWhileStatement(WhileStatement node);
	Node? VisitVarDeclarationStatement(VarDeclarationStatement node);
	Node? VisitReturnStatement(ReturnStatement node);
	Node? VisitExpressionStatement(ExpressionStatement node);
	Node? VisitBlock(Block node);
	Node? VisitFunctionCallExpression(FunctionCallExpression node);
	Node? VisitIndexingExpression(IndexingExpression node);
	Node? VisitSliceExpression(SliceExpression node);
	Node? VisitUnaryExpression(UnaryExpression node);
	Node? VisitArithmeticExpression(ArithmeticExpression node);
	Node? VisitComparisonExpression(ComparisonExpression node);
	Node? VisitTernaryExpression(TernaryExpression node);
	Node? VisitMatchExpression(MatchExpression node);
	Node? VisitMatchCaseExpression(MatchCaseExpression node);
	Node? VisitNewArrayExpression(NewArrayExpression node);
	Node? VisitNewObjExpression(NewObjExpression node);
	Node? VisitIdentifier(Identifier node);
	Node? VisitMemberIdentifier(MemberIdentifier node);
	Node? VisitCastExpression(CastExpression node);
	Node? VisitRefExpression(RefExpression node);
	Node? VisitCharLiteralExpression(CharLiteralExpression node);
	Node? VisitStringLiteralExpression(StringLiteralExpression node);
	Node? VisitIntegerLiteralExpression(IntegerLiteralExpression node);
	Node? VisitNullLiteralExpression(NullLiteralExpression node);
}
