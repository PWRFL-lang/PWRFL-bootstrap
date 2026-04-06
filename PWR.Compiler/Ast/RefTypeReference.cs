namespace PWR.Compiler.Ast;

public class RefTypeReference(TypeReference baseType) : TypeReference(baseType.Position)
{
	public TypeReference BaseType { get; } = baseType;

	public override NodeType Type => NodeType.RefTypeReference;

	public override void Accept(IVisitor visitor) => visitor.VisitRefTypeReference(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitRefTypeReference(this);
}
