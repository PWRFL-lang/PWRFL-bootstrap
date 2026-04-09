using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public interface IType
{
	string Name { get; }
	IType? ElementType => this is ICollectionType c ? c.BaseType : null;

	ISemantic? GetMember(string name) => null;
}
