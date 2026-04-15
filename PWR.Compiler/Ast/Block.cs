using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

// Utility class. The parser does not produce Block statements, but some transformers
// or macros might want to turn one statement into many. They can do so by encapsulating
// them in a Block
public class Block(Statement[] body) : Statement(body[0].Position), IScope
{
	public Statement[] Body { get; } = body;

	public Block With(Statement[] body) => new(body) { SymbolTable = SymbolTable };

	public override NodeType Type => NodeType.Block;

	public override void Accept(IVisitor visitor) => visitor.VisitBlock(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitBlock(this);

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
