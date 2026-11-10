using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Ast;

public class ClassDeclaration(Position pos, Identifier name, Identifier[] inherited, Declaration[] body)
	: TypeDeclaration(pos, name, body)
{
	public ClassDeclaration(Position pos, Identifier name, List<Identifier> inherited, List<Declaration> body)
		: this(pos, name, [.. inherited], body.ToArray())
	{ }

	public ClassDeclaration With(Annotation[] annotations, Identifier name, Identifier[] inherited, Declaration[] body)
		=> new(Position, name, inherited, body) {
			SymbolTable = SymbolTable,
			Annotations = annotations,
			Semantic = Semantic,
			IsAbstract = IsAbstract,
		};

	public override bool Lookup(string name, List<ISemantic> collector, SemanticType type)
	{
		var oldCount = collector.Count;
		collector.AddRange(SymbolTable.Where(s => ((uint)s.SemanticType & (uint)type) != 0 && s.Name == name));
		if (oldCount != collector.Count) {
			return true;
		}
		var current = ((IClassType)Type).BaseType;
		while (current != null) {
			var check = current.GetMember(name);
			if (check != null) {
				collector.Add(check);
				return true;
			} else {
				current = current.BaseType;
			}
		}
		return false;
	}

	public Identifier[] Inherited { get; } = inherited;

	public bool IsAbstract { get; init; }

	public override NodeType NodeType => NodeType.ClassDeclaration;

	public int FieldCount => SymbolTable.OfType<FieldDecl>().Where(f => !f.IsStatic).Count();

	public override void Accept(IVisitor visitor) => visitor.VisitClassDeclaration(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitClassDeclaration(this);
}
