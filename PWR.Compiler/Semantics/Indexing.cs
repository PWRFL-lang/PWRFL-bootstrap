using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Indexing(IndexingExpression node, IType type) : ISemantic
{
	public IndexingExpression Node { get; } = node;

	public string Name => "Indexing";

	public SemanticType SemanticType => SemanticType.Indexing;

	public IType Type { get; } = type;
}
