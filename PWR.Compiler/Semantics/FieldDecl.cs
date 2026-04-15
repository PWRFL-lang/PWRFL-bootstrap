using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class FieldDecl(FieldDeclaration node, TypeDeclaration td, int index) : IMemberSemantic
{
	public FieldDeclaration Node { get; } = node;
	public TypeDeclaration ParentType { get; } = td;
	public int Index { get; } = index;

	public string Name => Node.Decl.Name;
	public string FullName => $"{ParentType.Semantic!.FullName}${Name}";

	public SemanticType SemanticType => SemanticType.Field;

	public IType Type => Node.Decl.VarType!.Semantic!.Type ?? Node.Value?.Semantic?.Type ?? throw new CompileError(Node, "Internal compiler error: No type bound for this field");
	
	IType IMemberSemantic.ParentType => this.ParentType.Semantic!.Type;

	public bool IsStatic => Index < 0;
}
