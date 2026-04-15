using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public enum VarUsage
{
	Var,
	Let,
	Const
}

public class VarDeclaration(Position pos, string name, TypeReference? type) : Declaration(pos)
{
	public string Name { get; } = name;
	public TypeReference? VarType { get; } = type;

	public override NodeType Type => NodeType.VarDeclaration;

	public override void Accept(IVisitor visitor) => visitor.VisitVarDeclaration(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitVarDeclaration(this);

	public override string ToString() => VarType == null ? Name : $"{Name}: {VarType}";
}

public class VarDeclarationStatement(Position pos, VarDeclaration decl, Expression value, VarUsage varType) : Statement(pos), ISemanticNode
{
	public VarDeclaration Decl { get; } = decl;
	public Expression Value { get; } = value;
	public VarUsage VarType { get; } = varType;

	public override NodeType Type => NodeType.VarDeclarationStatement;

	public ISemantic? Semantic { get; set; }

	public override void Accept(IVisitor visitor) => visitor.VisitVarDeclarationStatement(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitVarDeclarationStatement(this);
}