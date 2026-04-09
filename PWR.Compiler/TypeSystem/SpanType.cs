using System;
using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public class SpanType : IType, ICollectionType
{
	public IType BaseType { get; }

	public string Name => BaseType.Name + " span";

	private readonly Dictionary<string, ISemantic> _members = new() {
		{ "Length",
			new MagicProperty(
				"Length",
				"span$Length",
				new SimpleTypeReference(default, "int") { Semantic = new TypeRef(Types.Int32) }
			)
		},
		{ "ToPtr",
			new MagicFunction(
				"ToPtr",
				"span$ToPtr",
				new SimpleTypeReference(default, "ptr") { Semantic = new TypeRef(Types.Ptr) }
			)
		}
	};

	private SpanType(IType baseType) => BaseType = baseType;

	ISemantic? IType.GetMember(string name)
	{
		_members.TryGetValue(name, out var result);
		return result;
	}

	private static readonly Dictionary<IType, SpanType> _cache = [];

	internal static SpanType Create(IType baseType)
	{
		if (!_cache.TryGetValue(baseType, out var result)) {
			result = new SpanType(baseType);
			_cache.Add(baseType, result);
		}
		return result;
	}
}
