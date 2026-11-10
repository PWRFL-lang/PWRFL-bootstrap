using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.TypeSystem;

// The type of a metaclass value, written `meta T`.
public class MetaclassType : IType
{
	public IClassType Class { get; }

	// Bare `meta`, which holds the metaclass of any class at all, is the metaclass type of Object.
	// Every class is an Object, so every metaclass is assignable to it.
	public bool IsAny => Class is ObjectType;

	public static MetaclassType Any => Create(ObjectType.Instance);

	public string Name => IsAny ? "meta" : "meta " + Class.Name;

	private MetaclassType(IClassType cls) => Class = cls;

	private static readonly Dictionary<IClassType, MetaclassType> _cache = [];

	public static MetaclassType Create(IClassType cls)
	{
		if (!_cache.TryGetValue(cls, out var result)) {
			result = new MetaclassType(cls);
			_cache.Add(cls, result);
		}
		return result;
	}

	public static MetaclassType For(IType type, Node site)
		=> type is IClassType cls
			? Create(cls)
			: throw new CompileError(site, $"'meta' can only be applied to a class, but '{type}' is not a class.");

	internal static void ClearCache() => _cache.Clear();

	// What can be reached through a metaclass value: its class's virtual constructors and
	// class-scope members (methods and constants).
	// Instance members need an instance, not a metaclass.
	//
	// Only a *virtual* constructor, because a metaclass value can hold any subclass, and the
	// constructor run has to be that subclass's own. A non-virtual constructor could skip the
	// subclass's constructors, and with them its field defaults and the definite-assignment checks
	// that ensure the object is constructed in a valid state.
	public ISemantic? GetMember(string name)
		=> !IsAny && Class.GetMember(name) is { } member && IsReachable(member)
			? member
			: null;

	private static bool IsReachable(ISemantic member)
		=> ClassScope.Is(member)
			|| (member.SemanticType.HasFlag(SemanticType.Constructor) && member is IMethod { Slot: >= 0 });

	public bool CanAssignTo(IType targetType)
		=> targetType == this || (targetType is MetaclassType other && Class.CanAssignTo(other.Class));

	public override string ToString() => Name;
}
