using PWR.Compiler.Ast;

namespace PWR.Compiler.Semantics;

internal static class ClassScope
{
	public static bool Is(ISemantic? member)
		=> member is IClassConstant || member is IFunction { Flags: var flags } && flags.HasFlag(FunctionFlags.ClassScope);
}
