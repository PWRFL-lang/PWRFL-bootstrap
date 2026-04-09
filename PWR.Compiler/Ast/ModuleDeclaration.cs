using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public class ModuleDeclaration(Position pos, Identifier name, Declaration[] body, VarDeclarationStatement[] init) 
	: TypeDeclaration(pos), IScope
{
	public Identifier Name { get; } = name;
	public Declaration[] Body { get; } = body;
	public VarDeclarationStatement[] Init { get; } = init;

	public ModuleDeclaration(Position pos, Identifier name, List<Declaration> body, List<VarDeclarationStatement> init)
		: this(pos, name, body.ToArray(), [..init])
	{ }

	public ModuleDeclaration With(Annotation[] annotations, Identifier name, Declaration[] body, VarDeclarationStatement[] init) 
		=> new(Position, name, body, init) {
			SymbolTable = SymbolTable,
			Annotations = annotations,
			Semantic = Semantic,
		};

	public override NodeType Type => NodeType.ModuleDeclaration;

	public override void Accept(IVisitor visitor) => visitor.VisitModuleDeclaration(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitModuleDeclaration(this);

	internal List<ISemantic> SymbolTable { get; init; } = [];

	public bool Lookup(string name, List<ISemantic> collector, SemanticType type)
	{
		var oldCount = collector.Count;
		collector.AddRange(SymbolTable.Where(s => ((uint)s.SemanticType & (uint)type) != 0 && s.Name == name));
		return oldCount != collector.Count;
	}

	void IScope.Add(ISemantic semantic) => SymbolTable.Add(semantic);
}
