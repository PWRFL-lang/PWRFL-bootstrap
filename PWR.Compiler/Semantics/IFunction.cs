using PWR.Compiler.Ast;

namespace PWR.Compiler.Semantics;

public interface IFunction
{
	TypeReference? ReturnType { get; } 
	ParameterDeclaration[] Args { get; }
}
