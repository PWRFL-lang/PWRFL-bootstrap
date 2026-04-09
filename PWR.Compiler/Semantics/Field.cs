using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Field(VarDeclarationStatement decl) : ISemantic
{
	public VarDeclarationStatement Decl { get; } = decl;
	public string Name => Decl.Decl.Name;

	public SemanticType SemanticType => SemanticType.Field;

	public IType Type => Decl.Decl.Semantic!.Type;
}
