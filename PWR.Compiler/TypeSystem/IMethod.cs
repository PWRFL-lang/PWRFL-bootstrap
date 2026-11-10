using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public interface IMethod: IFunction
{
	int Slot { get; }
	IType ParentType { get; }
}
