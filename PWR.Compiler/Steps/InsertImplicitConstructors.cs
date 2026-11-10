using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.External;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Steps;

// A virtual constructor is reached through a `meta` value, which can hold any subclass: the
// constructor that runs is the one in the VMT slot of the class the value holds. If a subclass
// that adds state doesn't have a constructor of its own in that slot, its base class's runs on an
// instance of it, and the subclass's fields are never set up.
//
// So a class that declares instance fields and inherits a virtual constructor it doesn't redeclare
// must override it:
//   * if every field it declares has a default value, there's nothing for the constructor to decide:
//     it is `constructor override Name(params)` that calls `super Name(params)` and then (as after any
//     `super` call) assigns the defaults. That's generated here, as a declaration like any other, so
//     that everything after this step treats it as an ordinary override;
//   * if some field has no default, only the class can say how to set it up, so it's an error.
//
// A class that declares no fields has nothing of its own to skip, and inherits the constructor as it is.
//
// Runs after BindMembers, so every type is bound, and before AssignVmtSlots, which needs to see the
// override.
internal class InsertImplicitConstructors : VisitorCompileStep
{
	private readonly BindMembers _binder = new();
	private readonly Dictionary<ClassDeclaration, CodeFile> _files = [];
	private readonly HashSet<ClassDeclaration> _done = [];
	private Project _project = null!;
	private CodeFile _file = null!;

	private sealed record VirtualConstructor(string Name, string Owner, ParameterDeclaration[] Parameters);

	public override void VisitCodeFile(CodeFile node)
	{
		_file = node;
		base.VisitCodeFile(node);
	}

	public override void VisitClassDeclaration(ClassDeclaration node)
	{
		_files[node] = _file;
		base.VisitClassDeclaration(node);
	}

	public override Project Run(Project tree)
	{
		_project = tree;
		Visit(tree);
		foreach (var cls in _files.Keys.ToArray()) {
			Process(cls);
		}
		return tree;
	}

	private void Process(ClassDeclaration decl)
	{
		if (!_done.Add(decl)) {
			return;
		}
		var cls = (InternalClass)decl.Semantic!.Type;
		if (cls.BaseType is InternalClass baseClass) {
			Process(baseClass.Decl);
		}
		var fields = decl.Body.OfType<FieldDeclaration>().Where(f => !f.Flags.HasFlag(FunctionFlags.ClassScope)).ToArray();
		if (fields.Length == 0) {
			return;
		}
		var declared = decl.Body.OfType<FunctionDeclaration>().Where(f => f.IsConstructor).Select(f => f.Name.Name).ToHashSet();
		foreach (var inherited in VirtualConstructors(cls.BaseType).Where(v => !declared.Contains(v.Name)).ToArray()) {
			var undefaulted = fields.Where(f => f.Value == null).Select(f => $"'{f.Decl.Name}'").ToArray();
			if (undefaulted.Length > 0) {
				var subject = undefaulted.Length == 1 ? $"field {undefaulted[0]} has" : $"fields {string.Join(", ", undefaulted)} have";
				throw new CompileError(decl,
					$"Class '{decl.Name}' must override the virtual constructor '{inherited.Owner}.{inherited.Name}': its {subject} no default value.");
			}
			var constructor = Synthesize(decl, inherited);
			decl.Body = [.. decl.Body, constructor];
		}
	}

	// The virtual constructors a class has: those it declares (a `virtual` one, or an `override`
	// of one), and those it inherits and doesn't redeclare. A constructor of the same name hides
	// the inherited one.
	private static IEnumerable<VirtualConstructor> VirtualConstructors(IClassType? cls)
	{
		switch (cls) {
			case InternalClass internalClass: {
				var declared = internalClass.Decl.Body.OfType<FunctionDeclaration>().Where(f => f.IsConstructor).ToArray();
				var own = declared
					.Where(f => f.Flags.HasFlag(FunctionFlags.Virtual) || f.Flags.HasFlag(FunctionFlags.Override))
					.Select(f => new VirtualConstructor(f.Name.Name, internalClass.Name, f.Parameters));
				return own.Concat(VirtualConstructors(internalClass.BaseType).Where(v => declared.All(d => d.Name.Name != v.Name)));
			}
			case ExternalClass externalClass: {
				var declared = externalClass.Methods.Where(m => ((ISemantic)m).SemanticType.HasFlag(SemanticType.Constructor)).ToArray();
				var own = declared
					.Where(m => m.Slot >= 0)
					.Select(m => new VirtualConstructor(((ISemantic)m).Name, externalClass.Name, [.. m.Args.Where(a => a.Name.Name != "self")]));
				return own.Concat(VirtualConstructors(externalClass.BaseType).Where(v => declared.All(d => ((ISemantic)d).Name != v.Name)));
			}
			default:
				return [];
		}
	}

	// `constructor override Name(parameters)` containing only `super Name(parameters)`, bound like any
	// declaration in the class. Its parameters are the inherited constructor's, with the types it already has.
	private FunctionDeclaration Synthesize(ClassDeclaration decl, VirtualConstructor inherited)
	{
		var pos = decl.Position;
		var parameters = inherited.Parameters
			.Select(p => new ParameterDeclaration(
				new Identifier(pos, p.Name.Name), CloneType(p.ParamType), p.DefaultValue == null ? null : BindExpressionsP3.CloneLiteral(p.DefaultValue)))
			.ToArray();
		Expression[] arguments = [.. parameters.Select(p => (Expression)new Identifier(pos, p.Name.Name))];
		Statement[] body = [new ExpressionStatement(new SuperCallExpression(pos, new Identifier(pos, inherited.Name), arguments))];
		var function = new FunctionDeclaration(pos, FunctionFlags.Override, new Identifier(pos, inherited.Name), parameters, null, body) { IsConstructor = true };
		new AssignParents().Assign(function, decl);
		_binder.Bind(function, decl, _files[decl], _project);
		return function;
	}

	private static TypeReference CloneType(TypeReference type) => type switch {
		SimpleTypeReference s => new SimpleTypeReference(s.Position, s.Name) { Semantic = s.Semantic },
		NilableTypeReference n => new NilableTypeReference(CloneType(n.BaseType)) { Semantic = n.Semantic },
		RefTypeReference r => new RefTypeReference(CloneType(r.BaseType)) { Semantic = r.Semantic },
		SpanTypeReference sp => new SpanTypeReference(CloneType(sp.BaseType)) { Semantic = sp.Semantic },
		SequenceTypeReference q => new SequenceTypeReference(CloneType(q.BaseType)) { Semantic = q.Semantic },
		ArrayTypeReference a => new ArrayTypeReference(CloneType(a.BaseType), a.Size == null ? null : BindExpressionsP3.CloneLiteral(a.Size)) { Semantic = a.Semantic },
		MetaTypeReference m => new MetaTypeReference(m.Position, m.BaseType == null ? null : CloneType(m.BaseType)) { Semantic = m.Semantic },
		_ => throw new NotImplementedException($"Copying the type '{type}'"),
	};
}
