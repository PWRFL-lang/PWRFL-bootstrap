namespace PWR.Compiler.Ast;

public class SpanTypeReference(TypeReference baseType) : TypeReference(baseType.Position)
{
	public TypeReference BaseType { get; } = baseType;

	public override NodeType Type => NodeType.SpanTypeReference;

	public override void Accept(IVisitor visitor) => visitor.VisitSpanTypeReference(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitSpanTypeReference(this);
}
