using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public class ArrayType : IType, ICollectionType
{
	public IType BaseType { get; }

	public string Name => BaseType.Name + " array";

	private ArrayType(IType baseType)
	{
		BaseType = baseType;
		_members = new() { 
			{ "Length",
				new MagicProperty(
					"Length",
					"arr$Length",
					new SimpleTypeReference(default, "int") { Semantic = new TypeRef(Types.Int32) }
				)
			},
			{
				"Resize",
				new MagicFunction(
					"Resize",
					"arr$Resize",
					new SimpleTypeReference(default, "void") { Semantic = new TypeRef(Types.Void) },
					[
						new ParameterDeclaration(
							new (default, "length"),
							new SimpleTypeReference(default, "int") { Semantic = new TypeRef(Types.Int32) })
					]
				)
			}
		};
	}

	private static readonly Dictionary<IType, ArrayType> _cache = [];
	internal static ArrayType Create(IType baseType)
	{
		if (!_cache.TryGetValue(baseType, out var result)) {
			result = new ArrayType(baseType);
			_cache.Add(baseType, result);
		}
		return result;
	}

	internal static void ClearCache() => _cache.Clear();

	private readonly Dictionary<string, ISemantic> _members;

	ISemantic? IType.GetMember(string name)
	{
		_members.TryGetValue(name, out var result);
		return result;
	}
}
