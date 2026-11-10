using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public interface IType
{
	string Name { get; }

	string FullName => Name;
	IType? ElementType => this is ICollectionType c ? c.ElementType : null;

	ISemantic? GetMember(string name) => null;
	bool CanAssignTo(IType targetType) => targetType == this;

	bool IsStruct => false;
}
