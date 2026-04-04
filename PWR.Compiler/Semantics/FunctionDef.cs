using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class FunctionDef(FunctionDeclaration decl) : ISemantic, IFunction
{
	public FunctionDeclaration Decl { get; } = decl;
	public string Name => Decl.Name.Name;

	public SemanticType SemanticType => SemanticType.Function;

	public IType Type => Decl.ReturnType?.Semantic?.Type ?? Types.Void;

	TypeReference? IFunction.ReturnType => Decl.ReturnType;

	ParameterDeclaration[] IFunction.Args => Decl.Parameters;
}
