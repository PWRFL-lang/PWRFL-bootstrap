using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using PWR.Compiler.Ast;

namespace PWR.Compiler.Analysis;

// Forward definite-assignment dataflow over structured code (no gotos, and no break/continue,
// so the shape of the AST is the shape of the control flow). It answers one question: at each
// place control can leave a block, which tracked keys have definitely been assigned?
//
// What "a key" is, and what counts as assigning one, is up to the subclass -- the walker knows
// nothing about fields or variables:
//   * TryGetTarget says whether the left side of an assignment is a tracked key.
//   * KeysAssignedBy lets a call expression assign keys as a side effect (a constructor that
//     calls a sibling constructor, for example).
// A subclass that also wants to check reads (use of an unassigned variable) can override the
// relevant Visit methods and consult State, which is always the state at the point being visited.
//
// Rules:
//   * Statements run in sequence; assignments accumulate.
//   * `if`/ternary/`match`: each arm starts from the state after the condition, and the state
//     afterwards is the join (intersection) of the arms. A missing `else` is an empty arm.
//   * Loops: the body may run zero times, so nothing it assigns definitely counts afterwards,
//     though it is possibly assigned. A later iteration sees what an earlier one did.
//   * A literal `true`/`false` condition makes the other arm unreachable. This makes
//     `while true` a loop with no normal exit.
//   * `return` records an exit and makes what follows unreachable.
internal abstract class DefiniteAssignmentWalker<TKey> : BaseVisitor where TKey : notnull
{
	private readonly List<AssignmentExit<TKey>> _exits = [];
	private int _revisitDepth;

	// The state at the node currently being visited.
	protected AssignmentState<TKey> State { get; set; } = AssignmentState<TKey>.Unreachable;

	// True while a loop body is being visited a second time, to see what a later iteration would
	// run into. Checks that look at State should still run (that's the point), but a subclass that
	// *collects* things as it goes should skip them, since the first pass already did.
	protected bool Revisiting => _revisitDepth > 0;

	// Analyzes a body that starts with `initiallyAssigned` already assigned, and returns every
	// reachable way out of it: each `return`, plus running off the end if possible (attributed to
	// `endSite`). Exits that are unreachable are left out.
	public IReadOnlyList<AssignmentExit<TKey>> Analyze(Statement[] body, IEnumerable<TKey> initiallyAssigned, Node endSite)
	{
		_exits.Clear();
		State = AssignmentState<TKey>.Reachable(initiallyAssigned);
		Visit(body);
		if (State.IsReachable) {
			_exits.Add(new(endSite, State));
		}
		return [.. _exits];
	}

	// Whether `target`, the left side of a plain assignment, is one of the tracked keys.
	protected abstract bool TryGetTarget(Expression target, [MaybeNullWhen(false)] out TKey key);

	// Keys that are definitely assigned once this call has been evaluated.
	protected virtual IEnumerable<TKey> KeysAssignedBy(Expression call) => [];

	public override void VisitAssignStatement(AssignStatement node)
	{
		Visit(node.Right);
		Visit(node.Left);
		// `x += 1` reads x before writing it; only the = operator definitely assigns.
		if (node.Op == AssignOperator.Assign && TryGetTarget(node.Left, out var key)) {
			State = State.Assign(key);
		}
	}

	public override void VisitFunctionCallExpression(FunctionCallExpression node)
	{
		base.VisitFunctionCallExpression(node);
		State = State.AssignAll(KeysAssignedBy(node));
	}

	public override void VisitSuperCallExpression(SuperCallExpression node)
	{
		base.VisitSuperCallExpression(node);
		State = State.AssignAll(KeysAssignedBy(node));
	}

	public override void VisitReturnStatement(ReturnStatement node)
	{
		base.VisitReturnStatement(node);
		if (State.IsReachable && !Revisiting) {
			_exits.Add(new(node, State));
		}
		State = AssignmentState<TKey>.Unreachable;
	}

	public override void VisitIfStatement(IfStatement node)
	{
		Visit(node.Cond);
		var (whenTrue, whenFalse) = Split(node.Cond);
		State = whenTrue;
		Visit(node.TrueBlock);
		var afterTrue = State;
		State = whenFalse;
		Visit(node.FalseBlock);
		State = afterTrue.Join(State);
	}

	public override void VisitTernaryExpression(TernaryExpression node)
	{
		Visit(node.Cond);
		var (whenTrue, whenFalse) = Split(node.Cond);
		State = whenTrue;
		Visit(node.Left);
		var afterLeft = State;
		State = whenFalse;
		Visit(node.Right);
		State = afterLeft.Join(State);
	}

	public override void VisitMatchExpression(MatchExpression node)
	{
		Visit(node.Value);
		var before = State;
		AssignmentState<TKey>? joined = null;
		foreach (var matchCase in node.Cases) {
			State = before;
			Visit(matchCase.Cases);
			Visit(matchCase.Value);
			joined = joined == null ? State : joined.Join(State);
		}
		State = joined ?? before;
	}

	public override void VisitWhileStatement(WhileStatement node)
	{
		Visit(node.Cond);
		var (whenTrue, whenFalse) = Split(node.Cond);
		State = whenTrue;
		var afterBody = VisitLoopBody(() => Visit(node.Body));
		State = LoopExit(whenFalse, afterBody);
	}

	public override void VisitForStatement(ForStatement node)
	{
		Visit(node.Coll);
		var before = State;
		var afterBody = VisitLoopBody(() => Visit(node.Body));
		State = LoopExit(before, afterBody);
	}

	// Visits a loop body twice, to find problems that might occur on subsequent passes
	private AssignmentState<TKey> VisitLoopBody(Action visitBody)
	{
		var head = State;
		visitBody();
		State = head.Join(State);
		_revisitDepth++;
		try {
			visitBody();
		} finally {
			_revisitDepth--;
		}
		return State;
	}

	// The loop exits when its condition is false, which can be after any number of iterations.
	// Because the body may not have run at all, nothing is definitely assigned by the loop, but
	// anything the body assigns is *possibly* assigned. A loop whose exit is unreachable
	// (`while true`) stays unreachable.
	private static AssignmentState<TKey> LoopExit(AssignmentState<TKey> whenExiting, AssignmentState<TKey> afterBody)
		=> whenExiting.IsReachable ? whenExiting.Join(afterBody) : whenExiting;

	// The states on the two arms of a condition. Normally both are the current state, but a
	// constant condition means one arm is unreachable.
	private (AssignmentState<TKey> WhenTrue, AssignmentState<TKey> WhenFalse) Split(Expression cond)
		=> cond is BooleanLiteralExpression { Value: var value }
			? (value ? State : AssignmentState<TKey>.Unreachable, value ? AssignmentState<TKey>.Unreachable : State)
			: (State, State);
}
