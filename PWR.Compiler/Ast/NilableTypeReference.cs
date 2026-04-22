namespace PWR.Compiler.Ast;

public class NilableTypeReference(TypeReference baseType) : TypeReference(baseType.Position)
{
	public TypeReference BaseType { get; } = baseType;

	public override NodeType Type => NodeType.NilableTypeReference;

	public override void Accept(IVisitor visitor) => visitor.VisitNilableTypeReference(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitNilableTypeReference(this);

	public override string ToString() => BaseType.ToString() + '?';
}
