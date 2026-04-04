using LLVMSharp.Interop;

namespace PWR.Compiler.TypeSystem;

internal class PrimitiveType(LLVMTypeRef type, string name) : IType
{
	public LLVMTypeRef Type => type;

	public string Name => name;

	public IType MakeArray() => new ArrayType(this);
	public IType MakeSpan() => new SpanType(this);

}
