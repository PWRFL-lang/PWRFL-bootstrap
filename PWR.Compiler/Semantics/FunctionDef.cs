using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class FunctionDef(FunctionDeclaration decl, bool hasSelf, ISemantic? owner = null) : ISemantic, IFunction
{
	public FunctionDeclaration Decl { get; } = decl;
	public ISemantic? Owner { get; } = owner;
	public string Name => Decl.Name.Name;
	public string FullName => Owner == null ? Name : $"{Owner.FullName}${Name}";

	public virtual SemanticType SemanticType => HasSelf ? SemanticType.Function | SemanticType.HasSelf : SemanticType.Function;

	public virtual IType Type => Decl.ReturnType?.Semantic?.Type ?? Types.Void;
	public bool HasSelf { get; } = hasSelf;

	TypeReference? IFunction.ReturnType => Decl.ReturnType;

	ParameterDeclaration[] IFunction.Args => Decl.Parameters;
}
