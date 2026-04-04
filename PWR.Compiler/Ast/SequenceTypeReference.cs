namespace PWR.Compiler.Ast;

public class SequenceTypeReference(TypeReference typ) : TypeReference(typ.Position)
{
	public TypeReference BaseType { get; } = typ;

	public override NodeType Type => NodeType.SequenceTypeReference;

	public override void Accept(IVisitor visitor) => visitor.VisitSequenceTypeReference(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitSequenceTypeReference(this);
}
