using System;

namespace PWR.Compiler.Semantics;

[Flags]
public enum SemanticType : uint
{
	Type       = 1,
	Function   = 2,
	Field      = 4,
	Property   = 8,
	Parameter  = 0x00000010,
	Variable   = 0x00000020,
	Constant   = 0x00000040,
	Unary      = 0x00000080,
	Comparison = 0x00000100,
	Arithmetic = 0x00000200,
	Ternary    = 0x00000400,
	Match      = 0x00000800,
	Indexing   = 0x00001000,
	Ref        = 0x00002000,

	EntityMask = 0x00FFFFFF,

	External   = 0x01000000,
	Magic      = 0x02000000,
	Global     = 0x04000000,

	All        = 0xFFFFFFFF,
}