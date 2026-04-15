using System;
using System.Collections.Generic;
using System.Diagnostics;

using LLVMSharp.Interop;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

internal class LValueVisitor(
	LLVMModuleRef module, 
	LLVMBuilderRef builder, 
	Stack<LLVMValueRef> values, 
	Dictionary<string, LLVMValueRef> locals, 
	Dictionary<string, LLVMValueRef> globals,
	Func<IType, LLVMTypeRef> lookupType) : IVisitor
{
	internal void Visit(Node node) => node.Accept(this);

	public void VisitAnnotation(Annotation node) => throw new NotImplementedException();

	public void VisitArithmeticExpression(ArithmeticExpression node) => throw new NotImplementedException();

	public void VisitArrayTypeReference(ArrayTypeReference node) => throw new NotImplementedException();

	public void VisitAssignStatement(AssignStatement node) => throw new NotImplementedException();

	public void VisitBlock(Block node) => throw new NotImplementedException();

	public void VisitCastExpression(CastExpression node) => throw new NotImplementedException();

	public void VisitCharLiteralExpression(CharLiteralExpression node) => throw new NotImplementedException();

	public void VisitCodeFile(CodeFile node) => throw new NotImplementedException();

	public void VisitComparisonExpression(ComparisonExpression node) => throw new NotImplementedException();

	public void VisitExpressionStatement(ExpressionStatement node) => throw new NotImplementedException();

	public void VisitFieldDeclaration(FieldDeclaration node) => throw new NotImplementedException();

	public void VisitForStatement(ForStatement node) => throw new NotImplementedException();

	public void VisitFunctionCallExpression(FunctionCallExpression node) => throw new NotImplementedException();

	public void VisitFunctionDeclaration(FunctionDeclaration node) => throw new NotImplementedException();

	public void VisitIdentifier(Identifier node)
	{
		switch (node.Semantic)
		{
			case null:
				break;
			case ParamDef:
			case VariableDecl:
				var ptr = locals[node.Semantic.Name];
				values.Push(builder.BuildLoad2(lookupType(node.Semantic.Type), ptr, "lVar_" + node.Semantic.Name));
				break;
			case GlobalFieldDecl gf:
				ptr = globals[node.Semantic.FullName];
				values.Push(builder.BuildLoad2(lookupType(node.Semantic.Type), ptr, "lVar_" + node.Semantic.Name));
				break;
			default:
				throw new CompileError(node, "Unknown Identifier semantic type");
		}
	}

	public void VisitIfStatement(IfStatement node) => throw new NotImplementedException();

	public void VisitIndexingExpression(IndexingExpression node)
	{
		var count = values.Count;
		Visit(node.Expr);
		Debug.Assert(values.Count == count + 1);
		var expr = values.Pop();
		var (span, elType) = node.Expr.Semantic!.Type switch {
			SpanType st => (builder.BuildExtractValue(expr, 0, "spanPtr"), lookupType(st.BaseType)),
			TypeSystem.ArrayType at => (builder.BuildExtractValue(expr, 0, "spanPtr"), lookupType(at.BaseType)),
			_ => throw new UnreachableException(),
		};

		//TODO: support multidimensional indexing
		var idxExpr = node.Indices[0];
		if (idxExpr is SliceExpression s) {
			throw new NotImplementedException();
		} else {
			VisitIndexing(span, elType, idxExpr);
		}
	}

	private void VisitIndexing(LLVMValueRef expr, LLVMTypeRef elType, Expression idxExpr)
	{
		var count = values.Count;
		Visit(idxExpr);
		Debug.Assert(values.Count == count + 1);
		var idx = values.Pop();
		var result = builder.BuildInBoundsGEP2(elType, expr, [idx], "index".AsSpan());
		values.Push(result);
	}

	public void VisitIntegerLiteralExpression(IntegerLiteralExpression node)
		=> values.Push(LLVMValueRef.CreateConstInt(module.Context.Int32Type, (ulong)node.Value));

	public void VisitMatchCaseExpression(MatchCaseExpression node) => throw new NotImplementedException();

	public void VisitMatchExpression(MatchExpression node) => throw new NotImplementedException();

	public void VisitMemberIdentifier(MemberIdentifier node) => throw new NotImplementedException();

	public void VisitModuleDeclaration(ModuleDeclaration node) => throw new NotImplementedException();

	public void VisitNewArrayExpression(NewArrayExpression node) => throw new NotImplementedException();

	public void VisitNewObjExpression(NewObjExpression node) => throw new NotImplementedException();

	public void VisitNullLiteralExpression(NullLiteralExpression node) => throw new NotImplementedException();

	public void VisitParameterDeclaration(ParameterDeclaration node) => throw new NotImplementedException();

	public void VisitProject(Project node) => throw new NotImplementedException();

	public void VisitRefExpression(RefExpression node) => throw new NotImplementedException();

	public void VisitRefTypeReference(RefTypeReference node) => throw new NotImplementedException();

	public void VisitReturnStatement(ReturnStatement node) => throw new NotImplementedException();

	public void VisitSequenceTypeReference(SequenceTypeReference node) => throw new NotImplementedException();

	public void VisitSimpleTypeReference(SimpleTypeReference node) => throw new NotImplementedException();

	public void VisitSliceExpression(SliceExpression node) => throw new NotImplementedException();

	public void VisitSpanTypeReference(SpanTypeReference node) => throw new NotImplementedException();

	public void VisitStringLiteralExpression(StringLiteralExpression node) => throw new NotImplementedException();

	public void VisitStructDeclaration(StructDeclaration node) => throw new NotImplementedException();

	public void VisitTernaryExpression(TernaryExpression node) => throw new NotImplementedException();

	public void VisitUnaryExpression(UnaryExpression node) => throw new NotImplementedException();

	public void VisitVarDeclaration(VarDeclaration node) => throw new NotImplementedException();

	public void VisitVarDeclarationStatement(VarDeclarationStatement node) => throw new NotImplementedException();

	public void VisitWhileStatement(WhileStatement node) => throw new NotImplementedException();

	public void VisitSelfLiteralExpression(SelfLiteralExpression node) => throw new NotImplementedException();
}
