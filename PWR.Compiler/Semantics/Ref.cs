using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Ref(RefExpression expr) : ISemantic
{
	public RefExpression Expr { get; } = expr;

	public string Name => "ref";

	public SemanticType SemanticType => SemanticType.Ref;

	public IType Type { get; } = RefType.Create(expr.Expr.Semantic!.Type);
}
