using System;

using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class MagicFunction(string name, string fullName, TypeReference returnType, params Span<ParameterDeclaration> args) : ISemantic, IFunction
{
	public TypeReference ReturnType { get; } = returnType;
	public ParameterDeclaration[] Args { get; } = [.. args];

	public string Name => name;
	public string FullName => fullName;

	public SemanticType SemanticType => SemanticType.Magic | SemanticType.Function;

	public IType Type => ReturnType.Semantic!.Type;
	public bool HasSelf => false;
}
