using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Semantics;

/// <summary>
/// Object's own trivial constructor. It has no declared source (Object is a compiler
/// built-in), takes no arguments, and does nothing but exist as the base case that every
/// class's constructor chain must eventually reach via `super Create()`.
/// </summary>
public class RootObjectConstructor : ISemantic, IFunction
{
	public static readonly RootObjectConstructor Instance = new();

	private RootObjectConstructor() { }

	public string Name => "Create";
	public string FullName => "Object$Create";
	public FunctionFlags Flags => FunctionFlags.None;

	public SemanticType SemanticType => SemanticType.Function | SemanticType.Constructor;

	public IType Type => ObjectType.Instance;

	TypeReference? IFunction.ReturnType => null;
	ParameterDeclaration[] IFunction.Args => [];
	bool IFunction.HasSelf => true;
}
