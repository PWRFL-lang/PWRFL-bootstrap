using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public interface ISemantic
{
	string Name { get; }
	string FullName => Name;
	SemanticType SemanticType { get; }
	IType Type { get; }
}

public interface ISemanticNode
{
	ISemantic? Semantic { get; }
}