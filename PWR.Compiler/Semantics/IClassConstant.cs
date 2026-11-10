namespace PWR.Compiler.Semantics;

public interface IClassConstant : IMemberSemantic
{
	bool IsAbstract { get; }
}
