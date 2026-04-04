using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class ParamDef(ParameterDeclaration param, int position) : ISemantic
{
	public ParameterDeclaration Param { get; } = param;
	public int Position { get; } = position;

	public string Name => Param.Name.Name;

	public SemanticType SemanticType => SemanticType.Parameter;

	public IType Type => Param.ParamType.Semantic?.Type!;
}
