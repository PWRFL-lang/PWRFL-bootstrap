namespace PWR.Compiler.Ast;

public class Annotation(Position pos, FunctionCallExpression call) : Node(pos)
{
	public FunctionCallExpression Call { get; } = call;

	public override NodeType Type => NodeType.Annotation;

	public override void Accept(IVisitor visitor) => visitor.VisitAnnotation(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitAnnotation(this);
}
