using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using PWR.Compiler.Analysis;
using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

// Validates constructors according to the following rules:
//   * On every path it must call exactly one base (`super`) or sibling (same-class) constructor.
//   * That call must come before anything touches `self`: its fields, its methods.
//   * On every path it must assign each of the class's own fields that has no default value.
//   * Delegation to sibling constructors must not be able to form cycles.
// The first three are all per-path questions about the same constructor body, so they're
// answered together by one definite-assignment walk (see ConstructorWalker).
internal class CheckConstructors : VisitorCompileStep
{
	private const string CTOR_CALLED = "<constructor call>";

	private const string MEMBER_BEFORE_CTOR_CALL = "Unable to access instance members before the base constructor has been called.";

	// The fields of the class being checked that its constructors are responsible for assigning:
	// declared on this class itself (inherited ones are the base constructor's job) and without a
	// default value (those start out assigned).
	private FieldDecl[] _fieldsToAssign = [];

	// The delegation graph: for each constructor, every sibling call in its body. There can be
	// several, on different paths (ie. the two arms of an `if` can each make their own).
	private readonly Dictionary<string, List<(string Callee, FunctionCallExpression Site)>> _delegations = [];

	public override void VisitCodeFile(CodeFile node)
	{
		_delegations.Clear();
		Visit(node.Decls);
		CheckForCycles();
	}

	// A constructor is only valid if *every* call it makes is either to a base constructor, which
	// ends the chain, or to a sibling that is itself valid. A cycle through any of the edges is
	// invalid, even if the constructors involved also have a path that reaches `super`, because
	// any path may be the one taken at runtime. Plain depth-first search: an edge back to a
	// constructor still on the current path closes a cycle.
	private void CheckForCycles()
	{
		var path = new List<string>();
		var finished = new HashSet<string>();

		void Search(string ctor)
		{
			if (finished.Contains(ctor)) {
				return;
			}
			path.Add(ctor);
			if (_delegations.TryGetValue(ctor, out var edges)) {
				foreach (var (callee, site) in edges) {
					var start = path.IndexOf(callee);
					if (start >= 0) {
						throw new CompileError(site, $"Detected a constructor cycle: {string.Join(" -> ", path.Skip(start).Append(callee))}.");
					}
					Search(callee);
				}
			}
			path.RemoveAt(path.Count - 1);
			finished.Add(ctor);
		}

		foreach (var ctor in _delegations.Keys) {
			Search(ctor);
		}
	}

	public override void VisitClassDeclaration(ClassDeclaration node)
	{
		_fieldsToAssign = [.. node.Body.OfType<FieldDeclaration>()
			.Where(f => f.Value == null && f.Semantic is FieldDecl { IsStatic: false })
			.Select(f => (FieldDecl)f.Semantic!)];
		Visit([.. node.Body.OfType<FunctionDeclaration>()]);
	}

	public override void VisitStructDeclaration(StructDeclaration node)
	{ }

	public override void VisitModuleDeclaration(ModuleDeclaration node)
	{ }

	public override void VisitFunctionDeclaration(FunctionDeclaration node)
	{
		if (!node.IsConstructor) {
			return;
		}
		var delegations = new List<(string Callee, FunctionCallExpression Site)>();
		var exits = new ConstructorWalker(_fieldsToAssign, delegations).Analyze(node.Body, [], node);
		if (delegations.Count > 0) {
			_delegations[((IMethod)node.Semantic!).FullName] = delegations;
		}
		CheckConstructorCalled(node, exits);
		CheckFieldsAssigned(node, exits);
	}

	private static void CheckConstructorCalled(FunctionDeclaration ctor, IReadOnlyList<AssignmentExit<string>> exits)
	{
		foreach (var exit in exits) {
			if (!exit.State.IsAssigned(CTOR_CALLED)) {
				var where = exit.Site is ReturnStatement ? "before this return" : "on every path";
				throw new CompileError(exit.Site, $"Constructor '{ctor.Name.Name}' must call a base or sibling constructor {where}.");
			}
		}
	}

	// Every path out of a constructor must have assigned each of the class's own fields that has
	// no default. A constructor that delegates to a sibling is covered by the sibling, which is
	// checked on its own.
	private void CheckFieldsAssigned(FunctionDeclaration ctor, IReadOnlyList<AssignmentExit<string>> exits)
	{
		if (_fieldsToAssign.Length == 0) {
			return;
		}
		foreach (var exit in exits) {
			var missing = _fieldsToAssign.Where(f => !exit.State.IsAssigned(f.FullName)).Select(f => $"'{f.Name}'").ToArray();
			if (missing.Length > 0) {
				var subject = missing.Length == 1 ? $"Field {missing[0]} is" : $"Fields {string.Join(", ", missing)} are";
				var where = exit.Site is ReturnStatement ? "at this return" : "on every path";
				throw new CompileError(exit.Site,
					$"{subject} not definitely assigned {where} through constructor '{ctor.Name.Name}'.");
			}
		}
	}

	private static bool IsSiblingConstructorCall(FunctionCallExpression node)
		=> node.Semantic!.SemanticType.HasFlag(SemanticType.Constructor)
			&& (node.Target is MemberIdentifier { ParentExpr: SelfLiteralExpression } || node.Target.NodeType == NodeType.Identifier);

	private static bool IsClassScope(ISemantic member) => ClassScope.Is(member);

	private static bool IsBaseConstructorCall(SuperCallExpression node)
		=> node.Semantic!.SemanticType.HasFlag(SemanticType.Constructor);

	private sealed class ConstructorWalker(FieldDecl[] fields, List<(string Callee, FunctionCallExpression Site)> delegations)
		: DefiniteAssignmentWalker<string>
	{
		private readonly string[] _allFields = [.. fields.Select(f => f.FullName)];

		protected override bool TryGetTarget(Expression target, [MaybeNullWhen(false)] out string key)
		{
			if (target is MemberIdentifier { ParentExpr: SelfLiteralExpression, Semantic: FieldDecl { IsStatic: false } field }) {
				key = field.FullName;
				return true;
			}
			key = null;
			return false;
		}

		// A base constructor call initializes the base part of the object, which is all this
		// constructor needs from it. A sibling call initializes the whole object, this class's
		// own fields included.
		protected override IEnumerable<string> KeysAssignedBy(Expression call) => call switch {
			FunctionCallExpression fc when IsSiblingConstructorCall(fc) => [CTOR_CALLED, .. _allFields],
			SuperCallExpression sc when IsBaseConstructorCall(sc) => [CTOR_CALLED],
			_ => [],
		};

		public override void VisitFunctionCallExpression(FunctionCallExpression node)
		{
			if (IsSiblingConstructorCall(node)) {
				RequireNoEarlierConstructorCall(node);
				if (!Revisiting) {
					delegations.Add((((IMethod)node.Semantic!).FullName, node));
				}
			}
			base.VisitFunctionCallExpression(node);
		}

		public override void VisitSuperCallExpression(SuperCallExpression node)
		{
			if (IsBaseConstructorCall(node)) {
				RequireNoEarlierConstructorCall(node);
			} else {
				// `super Method()` calls a method on self, which isn't valid until the object is initialized.
				RequireConstructorCalled(node, MEMBER_BEFORE_CTOR_CALL);
			}
			base.VisitSuperCallExpression(node);
		}

		public override void VisitMemberIdentifier(MemberIdentifier node)
		{
			if (node.ParentExpr is SelfLiteralExpression) {
				// A class-scope method works on the class, not the instance, so it needs no initialized object.
				if (node.Semantic is IMemberSemantic m && m.SemanticType.HasFlag(SemanticType.HasSelf) && !IsClassScope(m)) {
					RequireConstructorCalled(node, MEMBER_BEFORE_CTOR_CALL);
				}
				return;
			}
			base.VisitMemberIdentifier(node);
		}

		// Any other use of `self` outside a `MemberIdentifier` hands out the object itself -- as
		// an argument, stored somewhere, returned -- and whoever receives it can then use it.
		// Until the base constructor has run it is presumed not to be in a valid state
		public override void VisitSelfLiteralExpression(SelfLiteralExpression node)
		{
			RequireConstructorCalled(node, "'self' cannot be used before the base constructor has been called.");
			base.VisitSelfLiteralExpression(node);
		}

		// Both checks are about the paths reaching this node. Using `self` needs the call to have
		// happened on *every* one of them; making a call needs it to have happened on *none*.
		// Code that can't run (after a return) is exempt from both.
		private void RequireConstructorCalled(Node node, string message)
		{
			if (!State.IsAssigned(CTOR_CALLED)) {
				throw new CompileError(node, message);
			}
		}

		private void RequireNoEarlierConstructorCall(Node node)
		{
			if (State.IsPossiblyAssigned(CTOR_CALLED)) {
				throw new CompileError(node, "Only one call to a base or sibling constructor is permitted on any path through a constructor.");
			}
		}
	}
}
