using System;
using PWR.Compiler.Ast;

namespace PWR.Compiler.Parser;

internal class ParseError(Position position, string message) : Exception(message)
{
	public Position Position { get; } = position;
}