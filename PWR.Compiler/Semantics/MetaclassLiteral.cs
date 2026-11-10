using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class MetaclassLiteral(MetaclassExpression node, MetaclassType type) : ISemantic
{
	public MetaclassExpression Node { get; } = node;
	public IType Type { get; } = type;

	public string Name => $"Metaclass: {type.Name}";

	public SemanticType SemanticType => SemanticType.Constant;
}
