using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem.Internal;

public class InternalModule(ModuleDeclaration decl) : IType
{
	public string Name => Decl.Name.ToString();

	public ModuleDeclaration Decl { get; } = decl;

	ISemantic? IType.GetMember(string name) => Decl.Body.FirstOrDefault(d => d.Semantic?.Name == name)?.Semantic;

	public IType MakeArray()
	{
		throw new System.NotImplementedException();
	}

	public IType MakeSpan()
	{
		throw new System.NotImplementedException();
	}
}
