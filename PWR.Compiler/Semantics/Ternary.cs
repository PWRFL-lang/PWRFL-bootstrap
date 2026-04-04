using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Ternary(TernaryExpression node, IType type) : ISemantic
{
	public TernaryExpression Node { get; } = node;
	public IType Type { get; } = type;

	public string Name => "Ternary";

	public SemanticType SemanticType => SemanticType.Ternary;
}
