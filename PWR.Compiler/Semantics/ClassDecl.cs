using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Semantics;

public class ClassDecl(ClassDeclaration decl) : ISemantic
{
	public ClassDeclaration Decl { get; } = decl;
	public string Name => Decl.Name.ToString();

	public string? Namespace { get; init; }

	public string FullName => Namespace == null ? Name : $"{Namespace}$${Name}";

	public SemanticType SemanticType => SemanticType.Type;

	public IType Type { get; } = new InternalClass(decl);
}
