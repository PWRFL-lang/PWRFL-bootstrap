namespace PWR.Compiler.Ast;

public class MetaclassExpression(TypeReference type) : Expression(type.Position)
{
	public TypeReference Type { get; set; } = type;

	public override NodeType NodeType => NodeType.MetaclassExpression;

	public override bool IsLiteral => true;

	public override void Accept(IVisitor visitor) => visitor.VisitMetaclassExpression(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitMetaclassExpression(this);
}
