using PWR.Compiler.Ast;

namespace PWR.Compiler.Semantics;

public interface IFunction
{
	string FullName { get; }
	TypeReference? ReturnType { get; } 
	ParameterDeclaration[] Args { get; }
	bool HasSelf { get; }
	FunctionFlags Flags { get; }
	bool IsVirtual => (Flags & (FunctionFlags.Virtual | FunctionFlags.Abstract | FunctionFlags.ClassScope)) != FunctionFlags.None;
}
