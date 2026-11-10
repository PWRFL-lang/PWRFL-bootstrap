using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Steps;

// Builds every class's VMT. Runs once each class's base type and members are bound.
//
// A class's table starts as a copy of its base class's. Then, for each member in declaration
// order:
//   * `override`: replaces the entry with the same name. There must be one -- no overloading
//     means the name alone identifies the member being overridden -- and it has to be the same
//     kind of member.
//   * `virtual`/`abstract`/`class`: appends a new slot. (A class-scope member is always virtual.)
//   * none of those: not in the table; redeclaring an inherited virtual without `override` is an
//     error, rather than silently defining an unrelated member.
internal class AssignVmtSlots : VisitorCompileStep
{
	private enum MemberKind
	{
		InstanceMethod,
		ClassMethod,
		Constant,
	}

	private readonly List<InternalClass> _classes = [];
	private readonly HashSet<InternalClass> _done = [];

	public override void VisitClassDeclaration(ClassDeclaration node)
	{
		base.VisitClassDeclaration(node);
		_classes.Add((InternalClass)node.Semantic!.Type);
	}

	public override Project Run(Project tree)
	{
		Visit(tree);
		foreach (var cls in _classes) {
			Assign(cls);
		}
		return tree;
	}

	private void Assign(InternalClass cls)
	{
		if (!_done.Add(cls)) {
			return;
		}
		if (cls.BaseType is InternalClass baseClass) {
			Assign(baseClass);
		}

		var table = cls.BaseType.Vmt.ToList();
		var declaredHere = new HashSet<string>();
		foreach (var decl in cls.Decl.Body) {
			var (name, flags, isVirtual) = decl switch {
				FunctionDeclaration { Semantic: IFunction func } f => (f.Name.Name, func.Flags, func.IsVirtual),
				FieldDeclaration { Semantic: ClassConstantDecl } c => (c.Decl.Name, c.Flags, true),
				_ => (null, FunctionFlags.None, false),
			};
			if (name == null) {
				continue;
			}
			if (!declaredHere.Add(name)) {
				continue; // a duplicate member name; reported by the duplicate-member check, not here
			}
			var inherited = table.FindIndex(e => e.Name == name);
			if (flags.HasFlag(FunctionFlags.Override)) {
				if (inherited < 0) {
					throw new CompileError(decl, $"'{name}' is declared 'override', but there is no matching virtual member to override in a base class of '{cls.Name}'.");
				}
				var kind = KindOf(decl);
				if (KindOf(table[inherited]) != kind) {
					throw new CompileError(decl, $"'{cls.Name}.{name}' is {Describe(kind)}, but the member it overrides is {Describe(KindOf(table[inherited]))}.");
				}
				table[inherited] = EntryFor(decl, name, table[inherited]);
			} else if (isVirtual) {
				if (inherited >= 0) {
					throw new CompileError(decl, $"'{cls.Name}.{name}'must be declared 'override' to override the virtual member it inherits.");
				}
				table.Add(EntryFor(decl, name, null));
			} else if (inherited >= 0) {
				throw new CompileError(decl, $"'{cls.Name}.{name}' must be declared 'override' to override the virtual member it inherits.");
			}
		}
		// A class that isn't abstract has to be complete: otherwise an instance of it could reach
		// an abstract slot.
		if (!cls.IsAbstract) {
			var open = table.FindIndex(e => e.IsAbstract);
			if (open >= 0) {
				var declaredIn = table[open].FullName[..table[open].FullName.LastIndexOf('$')];
				declaredIn = declaredIn[(declaredIn.Contains("$$") ? declaredIn.IndexOf("$$") + 2 : 0)..];
				throw new CompileError(cls.Decl, $"Class '{cls.Name}' is not abstract, but does not implement the abstract member '{declaredIn}.{table[open].Name}'.");
			}
		}
		cls.Vmt = [.. table];
	}

	private static VmtEntry EntryFor(Declaration decl, string name, VmtEntry? overridden)
		=> decl is FieldDeclaration field ? ConstantEntry(field, name, overridden) : FunctionEntry(name, (IFunction)decl.Semantic!);

	private static VmtEntry FunctionEntry(string name, IFunction func)
		=> new(name, func.FullName, func.Flags.HasFlag(FunctionFlags.Abstract), func.Flags.HasFlag(FunctionFlags.ClassScope));

	private static VmtEntry ConstantEntry(FieldDeclaration decl, string name, VmtEntry? overridden)
	{
		var constant = (ClassConstantDecl)decl.Semantic!;
		var declared = decl.Decl.VarType?.Semantic?.Type;
		var inheritedType = overridden?.ConstantType;
		if (declared != null && inheritedType != null && declared != inheritedType) {
			throw new CompileError(decl, $"The constant '{name}' has type '{declared}', but the constant it overrides has type '{inheritedType}'.");
		}
		var type = declared ?? inheritedType ?? throw new CompileError(decl, $"'{name}' needs a type: const class {name}: T = value");
		if (!IsSupportedConstantType(type)) {
			throw new CompileError(decl, $"A class constant can't have type '{type}' yet. The types supported are int, byte, char, bool, string, and metaclasses.");
		}
		return new VmtEntry(name, constant.FullName, decl.Flags.HasFlag(FunctionFlags.Abstract),
			IsClassScope: false, IsConstant: true, ConstantType: type, Value: decl.Value);
	}

	private static bool IsSupportedConstantType(IType type)
		=> type == Types.Int32 || type == Types.Byte || type == Types.Char || type == Types.Bool
			|| type == Types.String || type == Types.Ptr || type is MetaclassType;

	private static MemberKind KindOf(Declaration decl) => decl switch {
		FieldDeclaration => MemberKind.Constant,
		FunctionDeclaration { Flags: var flags } when flags.HasFlag(FunctionFlags.ClassScope) => MemberKind.ClassMethod,
		_ => MemberKind.InstanceMethod,
	};

	private static MemberKind KindOf(VmtEntry entry)
		=> entry.IsConstant ? MemberKind.Constant : entry.IsClassScope ? MemberKind.ClassMethod : MemberKind.InstanceMethod;

	private static string Describe(MemberKind kind) => kind switch {
		MemberKind.Constant => "a constant",
		MemberKind.ClassMethod => "a class-scope method",
		_ => "an instance method",
	};
}
