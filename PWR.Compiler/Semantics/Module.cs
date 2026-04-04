using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Semantics;

public class Module(ModuleDeclaration decl) : ISemantic
{
	public ModuleDeclaration Decl { get; } = decl;
	public string Name => Decl.Name.ToString();

	public SemanticType SemanticType => SemanticType.Type;

	public IType Type { get; } = new InternalModule(decl);
}
