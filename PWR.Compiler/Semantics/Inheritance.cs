using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

internal static class Inheritance
{
	public static ISemantic? Find(IClassType? baseType, string name)
		=> baseType?.GetMember(name) is { } member && !member.SemanticType.HasFlag(SemanticType.Constructor) ? member : null;
}
