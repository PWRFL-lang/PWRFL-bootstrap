using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class ExternalClassConstant(string name, IType type, IType parent, bool isAbstract, int slot, Expression? value) : IClassConstant
{
	public string Name => name;
	public string FullName => $"{parent.FullName}${name}";

	public SemanticType SemanticType => SemanticType.Constant | SemanticType.HasSelf | SemanticType.External;

	public IType Type => type;
	public IType ParentType => parent;

	public bool IsAbstract => isAbstract;
	public int Slot => slot;
	public Expression? Value => value;
}
