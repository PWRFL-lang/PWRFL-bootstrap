using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public abstract class TypeDeclaration(Position pos, Identifier name, Declaration[] body)
	: Declaration(pos), IScope
{
	public Identifier Name { get; } = name;
	public Declaration[] Body { get; } = body;

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

	void IScope.Add(ISemantic semantic) => SymbolTable.Add(semantic);
}
