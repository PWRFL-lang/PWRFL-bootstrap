namespace PWR.Compiler.Semantics;

using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

public class NewArray(NewArrayExpression node, IType type) : ISemantic
{
	public NewArrayExpression Node { get; } = node;

	public string Name => "Array";

	public SemanticType SemanticType => throw new System.NotImplementedException();

	public IType Type { get; } = type;
}
