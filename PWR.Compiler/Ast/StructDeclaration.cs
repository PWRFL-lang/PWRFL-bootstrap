using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public class StructDeclaration(Position pos, Identifier name, Declaration[] body)
	: TypeDeclaration(pos, name, body)
{
	public StructDeclaration(Position pos, Identifier name, List<Declaration> body)
		: this(pos, name, body.ToArray())
	{ }

	public StructDeclaration With(Annotation[] annotations, Identifier name, Declaration[] body)
		=> new(Position, name, body) {
			SymbolTable = SymbolTable,
			Annotations = annotations,
			Semantic = Semantic,
		};

	public override NodeType Type => NodeType.StructDeclaration;

	public int FieldCount => SymbolTable.OfType<FieldDecl>().Where(f => !f.IsStatic).Count();

	public override void Accept(IVisitor visitor) => visitor.VisitStructDeclaration(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitStructDeclaration(this);
}
