using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem.Internal;

/// <summary>
/// The compiler-synthesized root of the class hierarchy. It has no fields of its own.
/// </summary>
public class ObjectType : IClassType
{
	public static readonly ObjectType Instance = new();

	private ObjectType() { }

	public string Name => "Object";

	public ISemantic[] Fields => [];

	public IClassType? BaseType => null;

	public IMethod[] Methods => [];

	public VmtEntry[] Vmt => [];

	public ISemantic? GetMember(string name) => name == "Create" ? RootObjectConstructor.Instance : null;

	public override string ToString() => Name;
}
