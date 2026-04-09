using System.Collections.Generic;

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
}
