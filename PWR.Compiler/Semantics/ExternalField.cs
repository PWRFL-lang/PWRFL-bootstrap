using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class ExternalField(string name, IType type, IType parent) : ISemantic
{
	public string Name => name;

	public SemanticType SemanticType => SemanticType.Field | SemanticType.External;

	public IType Type => type;

	public IType Parent => parent;
}
