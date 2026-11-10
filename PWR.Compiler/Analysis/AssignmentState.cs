using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using PWR.Compiler.Ast;

namespace PWR.Compiler.Analysis;

// Definite-assignment analysis state. It tracks two things about each key:
//   * Definitely assigned: assigned on *every* path reaching this point.
//   * Possibly assigned: assigned on *at least one* path reaching this point.
// Immutable, so a state can be captured at a branch and later joined with another branch.
//
// A state is also either reachable or not. Code after a `return` (or after a loop that can
// never exit) can't execute, so everything is vacuously definitely assigned there, and
// nothing is possibly assigned. Modelling this as the "unreachable" state, rather than as a
// full set of keys, means the analysis doesn't need to know the universe of keys up front, and
// makes Join work out right: joining with an unreachable path changes nothing.
internal sealed class AssignmentState<TKey> where TKey : notnull
{
	public static AssignmentState<TKey> Unreachable { get; } = new([], [], false);

	private readonly ImmutableHashSet<TKey> _definite;
	private readonly ImmutableHashSet<TKey> _possible;

	private AssignmentState(ImmutableHashSet<TKey> definite, ImmutableHashSet<TKey> possible, bool reachable)
	{
		_definite = definite;
		_possible = possible;
		IsReachable = reachable;
	}

	public static AssignmentState<TKey> Reachable(IEnumerable<TKey> assigned)
	{
		var keys = assigned.ToImmutableHashSet();
		return new(keys, keys, true);
	}

	public bool IsReachable { get; }

	// Assigned on every path to here. Vacuously true where nothing can reach.
	public bool IsAssigned(TKey key) => !IsReachable || _definite.Contains(key);

	// Assigned on at least one path to here.
	public bool IsPossiblyAssigned(TKey key) => IsReachable && _possible.Contains(key);

	public AssignmentState<TKey> Assign(TKey key)
		=> !IsReachable ? this : new(_definite.Add(key), _possible.Add(key), true);

	public AssignmentState<TKey> AssignAll(IEnumerable<TKey> keys)
	{
		if (!IsReachable) {
			return this;
		}
		var added = keys.ToImmutableHashSet();
		return new(_definite.Union(added), _possible.Union(added), true);
	}

	// The state where two paths meet again.
	public AssignmentState<TKey> Join(AssignmentState<TKey> other)
	{
		if (!IsReachable) {
			return other;
		}
		if (!other.IsReachable) {
			return this;
		}
		return new(_definite.Intersect(other._definite), _possible.Union(other._possible), true);
	}

	public override string ToString()
		=> IsReachable ? $"{{{string.Join(", ", _definite.Select(k => k.ToString()))}}}" : "unreachable";
}

// A point where control leaves the code being analyzed normally, along with what was definitely
// assigned there. Site is the node to blame when a requirement fails.
internal readonly record struct AssignmentExit<TKey>(Node Site, AssignmentState<TKey> State) where TKey : notnull;
