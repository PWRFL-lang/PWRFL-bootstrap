using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public class StringType() : InternalPrimitiveType("string"), IType
{
	private readonly Dictionary<string, ISemantic> _members = new() {
		{ "Length",
			new MagicProperty(
				"Length",
				"string$Length",
				new SimpleTypeReference(default, "int") { Semantic = new TypeRef(Types.Int32) }
			)
		}
	};

	ISemantic? IType.GetMember(string name)
	{
		_members.TryGetValue(name, out var result);
		return result;
	}
}
