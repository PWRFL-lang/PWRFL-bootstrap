namespace PWR.Compiler.Ast;

public class SimpleTypeReference(Position pos, string name) : TypeReference(pos)
{
	public string Name { get; } = name;

	public override NodeType Type => NodeType.SimpleTypeReference;

	public override void Accept(IVisitor visitor) => visitor.VisitSimpleTypeReference(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitSimpleTypeReference(this);
}
