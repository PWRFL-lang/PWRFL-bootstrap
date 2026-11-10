using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class ClassConstantDecl(FieldDeclaration node, ClassDeclaration owner) : IClassConstant
{
	public FieldDeclaration Node { get; } = node;
	public ClassDeclaration Owner { get; } = owner;

	public string Name => Node.Decl.Name;
	public string FullName => $"{Owner.Semantic!.FullName}${Name}";

	public SemanticType SemanticType => SemanticType.Constant | SemanticType.HasSelf;

	public bool IsAbstract => Node.Flags.HasFlag(FunctionFlags.Abstract);

	// The declared type, or for an override that doesn't repeat it, the type of the constant it overrides.
	public IType Type => Node.Decl.VarType?.Semantic?.Type ?? InheritedType();

	IType IMemberSemantic.ParentType => Owner.Semantic!.Type;

	private IType InheritedType()
	{
		var baseClass = ((IClassType)Owner.Semantic!.Type).BaseType;
		return baseClass?.GetMember(Name) is IClassConstant inherited
			? inherited.Type
			: throw new CompileError(Node, $"'{Name}' is declared 'override', but there is no constant to override in a base class of '{Owner.Name}'.");
	}
}
