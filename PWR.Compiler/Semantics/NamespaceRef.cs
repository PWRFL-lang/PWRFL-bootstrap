using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class NamespaceRef(Project project, string name) : ISemantic
{
	public string Name => name;

	public SemanticType SemanticType => SemanticType.Namespace;

	public IType Type { get; } = new NamespaceType(project, name);
}
