namespace PWR.Compiler.Ast;

public class ParameterDeclaration(Identifier name, TypeReference type, Expression? defaultValue = null) : Declaration(name.Position)
{
	public Identifier Name { get; } = name;
	public TypeReference ParamType { get; } = type;
	public Expression? DefaultValue { get; } = defaultValue;

	public override NodeType Type => NodeType.ParameterDeclaration;

	public override void Accept(IVisitor visitor) => visitor.VisitParameterDeclaration(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitParameterDeclaration(this);
}