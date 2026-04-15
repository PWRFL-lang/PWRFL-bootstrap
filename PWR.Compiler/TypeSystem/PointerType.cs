using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public class PointerType() : InternalPrimitiveType("ptr"), IType
{
	private readonly Dictionary<string, ISemantic> _members = new() {
		{ "AsSpan",
			new MagicFunction(
				"AsSpan",
				"ptr$AsSpan",
				new SpanTypeReference(
					new SimpleTypeReference(default, "byte") { Semantic = new TypeRef(Types.Byte) }
				) { Semantic = new TypeRef(SpanType.Create(Types.Byte)) },
				[
					new ParameterDeclaration(
						new (default, "length"),
						new SimpleTypeReference(default, "int") { Semantic = new TypeRef(Types.Int32) })
				]
			)
		}
	};

	ISemantic? IType.GetMember(string name)
	{
		_members.TryGetValue(name, out var result);
		return result;
	}
}
