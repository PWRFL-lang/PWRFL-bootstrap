using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public class ArrayType : IType, ICollectionType
{
	public IType BaseType { get; }

	public string Name => BaseType.Name + " array";

	private ArrayType(IType baseType) => BaseType = baseType;

	private static readonly Dictionary<IType, ArrayType> _cache = [];
	internal static ArrayType Create(IType baseType)
	{
		if (!_cache.TryGetValue(baseType, out var result)) {
			result = new ArrayType(baseType);
			_cache.Add(baseType, result);
		}
		return result;
	}

	private static readonly Dictionary<string, ISemantic> _members = new() {
		{ "Length",
			new MagicProperty(
				"Length",
				"arr$Length",
				new SimpleTypeReference(default, "int") { Semantic = new TypeRef(Types.Int32) }
			)
		},
	};

	ISemantic? IType.GetMember(string name)
	{
		_members.TryGetValue(name, out var result);
		return result;
	}
}
