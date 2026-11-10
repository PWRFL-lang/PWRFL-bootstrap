using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

public class MethodDef(FunctionDeclaration decl, bool hasSelf, ISemantic owner)
	: FunctionDef(decl, hasSelf, owner), IMemberSemantic, IMethod
{
	public IType ParentType => Owner!.Type;

	public int Slot => Owner?.Type is IClassType cls ? cls.SlotOf(Name) : -1;
}
