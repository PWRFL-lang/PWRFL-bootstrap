using System;

namespace PWR.Compiler.Semantics;

[Flags]
public enum SemanticType : uint
{
	Type       = 1,
	Function   = 2,
	Parameter  = 4,
	Variable   = 8,
	Constant   = 0x00000010,
	Comparison = 0x00000020,
	Unary      = 0x00000040,
	Arithmetic = 0x00000080,
	Ternary    = 0x00000100,
	Match      = 0x00000200,
	Indexing   = 0x00000400,

	External   = 0x01000000,
	Magic      = 0x02000000,

	All        = 0xFFFFFFFF,
}