using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public class ForStatement(Position pos, VarDeclaration index, Expression coll, Statement[] body) : Statement(pos), IScope
{
	public ForStatement(Position pos, VarDeclaration index, Expression coll, List<Statement> body)
		: this(pos, index, coll, body.ToArray())
	{ }
	public VarDeclaration Index { get; } = index;
	public Expression Coll { get; } = coll;
	public Statement[] Body { get; } = body;

	public ForStatement With(VarDeclaration index, Expression coll, Statement[] body)
		=> new(this.Position, index, coll, body) { SymbolTable = this.SymbolTable };

	public override NodeType Type => NodeType.ForStatement;

	public override void Accept(IVisitor visitor) => visitor.VisitForStatement(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitForStatement(this);

	internal List<ISemantic> SymbolTable { get; init; } = [];

	public bool Lookup(string name, List<ISemantic> collector, SemanticType type)
	{
		var oldCount = collector.Count;
		collector.AddRange(SymbolTable.Where(s => ((uint)s.SemanticType & (uint)type) != 0 && s.Name == name));
		return oldCount != collector.Count;
	}

	public bool Scan(Func<ISemantic, bool> predicate, List<ISemantic> collector, SemanticType type)
	{
		var oldCount = collector.Count;
		collector.AddRange(SymbolTable.Where(s => ((uint)s.SemanticType & (uint)type) != 0 && predicate(s)));
		return oldCount != collector.Count;
	}

	public void Add(ISemantic semantic) => SymbolTable.Add(semantic);
}
