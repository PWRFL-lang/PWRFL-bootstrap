using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public interface ICompositeType : IType
{
	ISemantic[] Fields { get; }
}
