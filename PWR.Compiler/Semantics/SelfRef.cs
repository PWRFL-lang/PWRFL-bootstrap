using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class SelfRef(SelfLiteralExpression node, IType selfType) : ISemantic
{
	public string Name => Type.Name;

	public SemanticType SemanticType => SemanticType.Parameter;

	public IType Type => selfType;
}
