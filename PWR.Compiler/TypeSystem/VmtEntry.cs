using PWR.Compiler.Ast;

namespace PWR.Compiler.TypeSystem;

public readonly record struct VmtEntry(
	string Name,
	string FullName,
	bool IsAbstract,
	bool IsClassScope = false,
	bool IsConstant = false,
	IType? ConstantType = null,
	Expression? Value = null);
