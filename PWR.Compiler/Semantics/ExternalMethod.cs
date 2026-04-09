using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

internal class ExternalMethod(string name, IType[] sig, IType owner) : ISemantic, IFunction
{
	public string Name => name;

	public SemanticType SemanticType => SemanticType.Function | SemanticType.External;

	public IType Type => sig[^1];

	public IType Parent => owner;

	public TypeReference? ReturnType => throw new System.NotImplementedException();

	public ParameterDeclaration[] Args => throw new System.NotImplementedException();
}
