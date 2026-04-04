using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class Match(MatchExpression node, IType typ) : ISemantic
{
	public MatchExpression Node { get; } = node;
	public IType Typ { get; } = typ;

	public string Name => "Match";

	public SemanticType SemanticType => SemanticType.Match;

	public IType Type => Typ;
}
