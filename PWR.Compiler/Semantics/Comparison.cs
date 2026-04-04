using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Comparison(ComparisonExpression node) : ISemantic
{
	public ComparisonExpression Node { get; } = node;

	public string Name => "Comparison";

	public SemanticType SemanticType => SemanticType.Comparison;

	public IType Type => Types.Bool;
}
