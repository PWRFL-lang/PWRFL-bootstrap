using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

internal interface ISetTypeSemantic
{
	IType Type { get; set; }
}