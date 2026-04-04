using System;

using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class ExternalFunction(string name, string fullName, TypeReference returnType, params Span<ParameterDeclaration> args) : ISemantic, IFunction
{
	public TypeReference ReturnType { get; } = returnType;
	public ParameterDeclaration[] Args { get; } = [.. args];

	public string Name => name;
	public string FullName => fullName;

	public SemanticType SemanticType => SemanticType.External | SemanticType.Function;

	public IType Type => ReturnType.Semantic?.Type ?? Types.Void;
}
