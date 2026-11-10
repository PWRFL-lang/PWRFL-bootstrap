using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class ExternalField(string name, IType type, IType parent, Expression? constant = null, int index = -1) : IMemberSemantic
{
	public Expression? Constant { get; } = constant;

	public string Name { get; } = name;

	public SemanticType SemanticType => SemanticType.Field | SemanticType.External;

	public IType Type { get; } = type;

	public IType Parent { get; } = parent;

	IType IMemberSemantic.ParentType => Parent;

	public int Index { get; } = index;
}
