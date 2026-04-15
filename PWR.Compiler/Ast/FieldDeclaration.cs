namespace PWR.Compiler.Ast;

public class FieldDeclaration(Position pos, VarDeclaration decl, Expression? value, VarUsage varType) : Declaration(pos)
{
	public VarDeclaration Decl { get; } = decl;
	public Expression? Value { get; } = value;
	public VarUsage VarType { get; } = varType;

	public override NodeType Type => NodeType.VarDeclarationStatement;

	public override void Accept(IVisitor visitor) => visitor.VisitFieldDeclaration(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitFieldDeclaration(this);
}