using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public interface IMemberSemantic : ISemantic
{
	IType ParentType { get; }
}
