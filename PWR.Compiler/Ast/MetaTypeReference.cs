namespace PWR.Compiler.Ast;

public class MetaTypeReference(Position pos, TypeReference? baseType) : TypeReference(pos)
{
	public TypeReference? BaseType { get; } = baseType;

	public override NodeType NodeType => NodeType.MetaTypeReference;

	public override void Accept(IVisitor visitor) => visitor.VisitMetaTypeReference(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitMetaTypeReference(this);
}
