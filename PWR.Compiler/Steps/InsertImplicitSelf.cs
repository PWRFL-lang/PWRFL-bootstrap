using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

public class InsertImplicitSelf : TransformerCompileStep
{
	public override Node? VisitAnnotation(Annotation node) => node;

	public override Node? VisitIdentifier(Identifier node)
	{
		var sem = node.Semantic!;
		if (node.Type == NodeType.Identifier //not a member identifier
			&& sem != null 
			&& ((sem.SemanticType.HasFlag(SemanticType.Field) && !sem.SemanticType.HasFlag(SemanticType.Global))
			 || sem.SemanticType.HasFlag(SemanticType.HasSelf))
		) {
			return new MemberIdentifier(
				new SelfLiteralExpression(node.Position) {
					Semantic = new TypeRef(((IMemberSemantic)node.Semantic!).ParentType) 
				}, 
				node.Name) { Semantic = node.Semantic };
		}
		return node;
	}

	public override Node? VisitFunctionDeclaration(FunctionDeclaration node)
	{
		if (node.Semantic is MethodDef { HasSelf: true } fd && node.Parameters is not [{ Name.Name: "self" }, ..]) {
			var body = Visit(node.Body) ?? [];
			var implicitSelf = new ParameterDeclaration(
					new Identifier(default, "self"),
					new SimpleTypeReference(default, fd.Owner!.Type.Name) { Semantic = new TypeRef(fd.Owner.Type) });
			implicitSelf.Semantic = new ParamDef(implicitSelf, 0);
			for (int i = 0; i < node.Parameters.Length; ++i) {
				var param = node.Parameters[i];
				param.Semantic = new ParamDef(param, i + 1);
			}
			ParameterDeclaration[] parameters = [implicitSelf, .. node.Parameters];
			var result = node.With(node.Name, parameters, node.ReturnType, body);
			result.Semantic = new MethodDef(result, true, fd.Owner);
			return result;
		}
		return base.VisitFunctionDeclaration(node);
	}
}
