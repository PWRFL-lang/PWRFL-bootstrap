using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Literal(LiteralExpression node, IType type) : ISemantic
{
	public LiteralExpression Node { get; } = node;
	public IType Type { get; } = type;

	public string Name => "";

	public SemanticType SemanticType => SemanticType.Constant;
}
