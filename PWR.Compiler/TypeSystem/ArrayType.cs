using System.Collections.Generic;

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
}
