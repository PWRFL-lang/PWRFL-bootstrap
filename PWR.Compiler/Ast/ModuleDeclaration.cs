using System.Collections.Generic;

namespace PWR.Compiler.Ast;

public class ModuleDeclaration(Position pos, Identifier name, Declaration[] body, TypeReference? extendType) 
	: TypeDeclaration(pos, name, body)
{
	public TypeReference? ExtendType { get; } = extendType;

	public ModuleDeclaration(Position pos, Identifier name, List<Declaration> body, TypeReference? extendType)
		: this(pos, name, body.ToArray(),  extendType)
	{ }

	public ModuleDeclaration With(Annotation[] annotations, Identifier name, Declaration[] body, TypeReference? extendType)
		=> new(Position, name, body, extendType) {
			SymbolTable = SymbolTable,
			Annotations = annotations,
			Semantic = Semantic,
		};

	public override NodeType Type => NodeType.ModuleDeclaration;

	public override void Accept(IVisitor visitor) => visitor.VisitModuleDeclaration(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitModuleDeclaration(this);
}
