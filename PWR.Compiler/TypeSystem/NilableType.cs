using System.Collections.Generic;

namespace PWR.Compiler.TypeSystem;

public class NilableType : IType
{
	public IType BaseType { get; }

	public string Name => BaseType.Name + '?';

	private NilableType(IType baseType) => BaseType = baseType;

	private static readonly Dictionary<IType, NilableType> _cache = [];

	public static NilableType Create(IType baseType)
	{
		if (!_cache.TryGetValue(baseType, out var result)) {
			result = new NilableType(baseType);
			_cache.Add(baseType, result);
		}
		return result;
	}

	public override string ToString() => Name;
}
