using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Arithmetic(ArithmeticExpression node, IType type) : ISemantic
{
	public ArithmeticExpression Node { get; } = node;
	public IType Type { get; } = type;

	public string Name => "Arithmetic";

	public SemanticType SemanticType => SemanticType.Arithmetic;
}
