using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Unary(UnaryExpression node) : ISemantic
{
	public UnaryExpression Node { get; } = node;

	public string Name => "Unary";

	public SemanticType SemanticType => SemanticType.Unary;

	public IType Type => Node.Expr.Semantic!.Type;
}
