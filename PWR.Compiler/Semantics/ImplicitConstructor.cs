using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class ImplicitConstructor : ISemantic, IFunction
{
	public ImplicitConstructor(ICompositeType type)
	{
		Type = type;
		ReturnType = new SimpleTypeReference(default, type.Name) { Semantic = new TypeRef(type) };
		Args = [.. type.Fields.Select(BuildFieldParam)];
	}

	private static ParameterDeclaration BuildFieldParam(ISemantic sem) 
		=> new (
			new Identifier(default, sem.Name),
			new SimpleTypeReference(default, sem.Type.Name) { Semantic = new TypeRef(sem.Type) });

	public string Name => $"{Type.Name}$$iCtor";

	public SemanticType SemanticType => SemanticType.Constructor | SemanticType.Function | SemanticType.Magic;

	public IType Type { get; }
	public bool HasSelf => false;
	public TypeReference? ReturnType { get; }

	public ParameterDeclaration[] Args { get; }
}
