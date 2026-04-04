using System;

using PWR.Compiler.Ast;

namespace PWR.Compiler;

public class CompileError(Node node, string message) : Exception(message)
{
	public Node Node { get; } = node;
}
