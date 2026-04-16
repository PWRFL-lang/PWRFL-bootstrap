using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class MethodDef(FunctionDeclaration decl, bool hasSelf, ISemantic owner)
	: FunctionDef(decl, hasSelf, owner), IMemberSemantic, IFunction
{
	public IType ParentType => Owner!.Type;
}
