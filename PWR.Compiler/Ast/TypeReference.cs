using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Ast;

public abstract class TypeReference(Position pos) : Node(pos), ISemanticNode
{
	public ISemantic? Semantic { get; set; }
	public TypeReference WithType(IType type)
	{
		Semantic = new TypeRef(type);
		return this;
	}
}