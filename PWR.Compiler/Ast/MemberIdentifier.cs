namespace PWR.Compiler.Ast;

public class MemberIdentifier(Expression parent, string name): Identifier(parent.Position, name)
{
	public Expression ParentExpr { get; } = parent;

	public override NodeType Type => NodeType.MemberIdentifier;

	public override Node? Accept(ITransformer visitor) => visitor.VisitMemberIdentifier(this);

	public override void Accept(IVisitor visitor) => visitor.VisitMemberIdentifier(this);

	public override string ToString() => $"{ParentExpr}.{Name}";

	internal MemberIdentifier With(Expression parent)
		=> new(parent, Name) { Semantic = this.Semantic };
}
