using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class VariableDecl(VarDeclaration decl) : ISemantic, ISetTypeSemantic
{
	public VarDeclaration Decl { get; } = decl;

	public string Name => Decl.Name;

	public SemanticType SemanticType => SemanticType.Variable;

	public IType Type { get; set; } = null!;
}
