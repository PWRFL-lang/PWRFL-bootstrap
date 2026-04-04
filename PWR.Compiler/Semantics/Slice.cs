using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Slice(SliceExpression node) : ISemantic
{
	public SliceExpression Node { get; } = node;

	public string Name => "Slice";
	public SemanticType SemanticType => throw new System.NotImplementedException();
	public IType Type => Types.Void;
}
