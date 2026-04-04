using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class NewObject(NewObjExpression node, IType type) : ISemantic
{
	public NewObjExpression Node { get; } = node;
	public IType Type { get; } = type;

	public string Name => "New Object";

	public SemanticType SemanticType => throw new System.NotImplementedException();
}
