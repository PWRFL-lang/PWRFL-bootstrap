using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

public class InsertImplicitSelf : TransformerCompileStep
{
	private bool _inClassScope;

	public override Node? VisitAnnotation(Annotation node) => node;

	public override Node? VisitIdentifier(Identifier node)
	{
		var sem = node.Semantic!;
		if (node.NodeType == NodeType.Identifier //not a member identifier
			&& sem != null 
			&& ((sem.SemanticType.HasFlag(SemanticType.Field) && !sem.SemanticType.HasFlag(SemanticType.Global))
			 || sem.SemanticType.HasFlag(SemanticType.HasSelf))
		) {
			var member = (IMemberSemantic)node.Semantic!;
			var receiverType = member.ParentType;
			if (_inClassScope) {
				if (!ClassScope.Is(member)) {
					throw new CompileError(node, $"'{node.Name}' is an instance member, and cannot be used from a class-scope method.");
				}
				receiverType = MetaclassType.Create((IClassType)receiverType);
			}
			return new MemberIdentifier(
				new SelfLiteralExpression(node.Position) { Semantic = new TypeRef(receiverType) },
				node.Name) { Semantic = node.Semantic };
		}
		return node;
	}

	public override Node? VisitFunctionDeclaration(FunctionDeclaration node)
	{
		if (node.Semantic is MethodDef { HasSelf: true } fd && node.Parameters is not [{ Name.Name: "self" }, ..]
			&& (!node.IsConstructor || !fd.Owner!.Type.IsStruct)) {
			var classScope = fd.Flags.HasFlag(FunctionFlags.ClassScope);
			var enclosing = _inClassScope;
			_inClassScope = classScope;
			Statement[] body;
			try {
				body = Visit(node.Body) ?? [];
			} finally {
				_inClassScope = enclosing;
			}
			var selfType = fd.Owner!.Type;
			if (classScope) {
				selfType = MetaclassType.Create((IClassType)selfType);
			}
			if (selfType.IsStruct) {
				selfType = RefType.Create(selfType);
			}
			var implicitSelf = new ParameterDeclaration(
					new Identifier(default, "self"),
					new SimpleTypeReference(default, classScope ? selfType.Name : fd.Owner.Type.Name) { Semantic = new TypeRef(selfType) });
			implicitSelf.Semantic = new ParamDef(implicitSelf, 0);
			for (int i = 0; i < node.Parameters.Length; ++i) {
				var param = node.Parameters[i];
				param.Semantic = new ParamDef(param, i + 1);
			}
			ParameterDeclaration[] parameters = [implicitSelf, .. node.Parameters];
			var result = node.With(node.Name, parameters, node.ReturnType, body);
			result.Semantic = fd is Constructor ? new Constructor(result, fd.Owner!) : new MethodDef(result, true, fd.Owner);
			return result;
		}
		return base.VisitFunctionDeclaration(node);
	}
}
