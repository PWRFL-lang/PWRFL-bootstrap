using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Constructor(FunctionDeclaration node, ISemantic owner) : MethodDef(node, true, owner), ISemantic
{
	public override IType Type => ParentType;
	public override SemanticType SemanticType => SemanticType.Function | SemanticType.Constructor;
}