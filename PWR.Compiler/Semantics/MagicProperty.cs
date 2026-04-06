using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class MagicProperty(string name, string fullName, TypeReference typeRef) : ISemantic
{
	public string Name { get; } = name;
	public string FullName { get; } = fullName;
	public TypeReference TypeRef { get; } = typeRef;

	public SemanticType SemanticType => SemanticType.Property | SemanticType.Magic;

	public IType Type => TypeRef.Semantic!.Type;
}
