using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class GlobalFieldDecl(VarDeclaration node, ModuleDeclaration md) : ISemantic, ISetTypeSemantic
{
	public VarDeclaration Node { get; } = node;
	public ModuleDeclaration Md { get; } = md;

	public string Name => Node.Name;
	public string FullName => $"{Md.Semantic!.FullName}${Name}";

	public SemanticType SemanticType => SemanticType.Field | SemanticType.Global;

	public IType Type { get; set; } = null!;
}
