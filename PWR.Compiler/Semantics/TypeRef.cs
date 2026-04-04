using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class TypeRef(IType type) : ISemantic
{
	public string Name => Type.Name;

	public SemanticType SemanticType => SemanticType.Type;

	public IType Type { get; } = type;
}
