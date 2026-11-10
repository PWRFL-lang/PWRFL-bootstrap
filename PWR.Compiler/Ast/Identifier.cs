namespace PWR.Compiler.Ast;

public class Identifier(Position pos, string name) : Expression(pos)
{
	public string Name { get; } = name;
	public override NodeType NodeType => NodeType.Identifier;

	public override void Accept(IVisitor visitor) => visitor.VisitIdentifier(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitIdentifier(this);

	public override string ToString() => Name;
}
