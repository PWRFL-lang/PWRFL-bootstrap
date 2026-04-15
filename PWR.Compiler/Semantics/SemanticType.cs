using System;

namespace PWR.Compiler.Semantics;

[Flags]
public enum SemanticType : uint
{
	Type        = 1,
	Function    = 2,
	Field       = 4,
	Property    = 8,
	Constructor = 0x00000010,
	Destructor  = 0x00000020,
	Parameter   = 0x00000040,
	Variable    = 0x00000080,
	Constant    = 0x00000100,
	Unary       = 0x00000200,
	Comparison  = 0x00000400,
	Arithmetic  = 0x00000800,
	Ternary     = 0x00001000,
	Match       = 0x00002000,
	Indexing    = 0x00004000,
	Ref         = 0x00008000,

	EntityMask  = 0x00FFFFFF,

	External    = 0x01000000,
	Magic       = 0x02000000,
	Global      = 0x04000000,
	HasSelf     = 0x08000000,

	All         = 0xFFFFFFFF,
}