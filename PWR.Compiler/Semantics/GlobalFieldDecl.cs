using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class GlobalFieldDecl(FieldDeclaration node, ModuleDeclaration md) : ISemantic
{
	public FieldDeclaration Node { get; } = node;
	public ModuleDeclaration Md { get; } = md;

	public string Name => Node.Decl.Name;
	public string FullName => $"{Md.Semantic!.FullName}${Name}";

	public SemanticType SemanticType => SemanticType.Field | SemanticType.Global;

	public IType Type => Node.Decl.VarType?.Semantic?.Type ?? Node.Value?.Semantic?.Type ?? throw new CompileError(Node, "Internal compiler error: No type bound for this field");
}
