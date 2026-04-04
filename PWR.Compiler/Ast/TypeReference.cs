using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public abstract class TypeReference(Position pos) : Node(pos), ISemanticNode
{
	public ISemantic? Semantic { get; set; }
}