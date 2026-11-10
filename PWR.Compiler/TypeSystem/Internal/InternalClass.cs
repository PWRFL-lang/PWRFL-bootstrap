using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem.Internal;

public class InternalClass(ClassDeclaration decl) : IClassType
{
	public string Name => Decl.Name.ToString();

	public string FullName => Decl.Semantic?.FullName ?? Name;

	public ClassDeclaration Decl { get; } = decl;

	public IClassType BaseType { get; set; } = ObjectType.Instance;

	public ISemantic[] Fields
	{
		get {
			// (A class constant is a VMT slot, not a field of the instance.)
			field ??= [.. Decl.Body.OfType<FieldDeclaration>().Where(d => !d.Flags.HasFlag(FunctionFlags.ClassScope)).Select(d => d.Semantic!)];
			return field;
		}
	}

	public IMethod[] Methods
	{
		get
		{
			field ??= [.. Decl.Body.OfType<FunctionDeclaration>().Select(d => (IMethod)d.Semantic!)];
			return field;
		}
	}

	public VmtEntry[] Vmt { get; internal set; } = [];

	public bool IsAbstract => Decl.IsAbstract;

	// What the class declares, or else what its base class has (see Inheritance).
	ISemantic? IType.GetMember(string name)
		=> Decl.Body.FirstOrDefault(d => d.Semantic?.Name == name)?.Semantic ?? Inheritance.Find(BaseType, name);

	public bool CanAssignTo(IType targetType) => targetType == this || InheritsFrom(targetType);

	private bool InheritsFrom(IType targetType)
	{
		var typ = (IClassType)BaseType;
		while (typ != null) {
			if (typ == targetType) {
				return true;
			}
			typ = (IClassType?)typ.BaseType;
		}
		return false;
	}

	public override string ToString() => Name;
}
