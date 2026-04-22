using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public class RefType : IType
{
	public IType BaseType { get; }

	public string Name => BaseType.Name + " ref";

	private RefType(IType baseType) => BaseType = baseType;

	private static readonly Dictionary<IType, RefType> _cache = [];

	public static RefType Create(IType baseType)
	{
		if (!_cache.TryGetValue(baseType, out var result)) {
			result = new RefType(baseType);
			_cache.Add(baseType, result);
		}
		return result;
	}

	private readonly Dictionary<string, ISemantic> _members = new() {
		{ "AsSpan",
			new MagicFunction(
				"AsSpan",
				"ref$AsSpan",
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

	public override string ToString() => Name;
}
