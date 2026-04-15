using System;
using System.Collections.Generic;

namespace PWR.Compiler.Semantics;

public interface IScope
{
	void Add(ISemantic semantic);
	bool Lookup(string name, List<ISemantic> collector, SemanticType type);
	bool Lookup(string name, List<ISemantic> collector)
		=> Lookup(name, collector, SemanticType.All);

	bool Scan(Func<ISemantic, bool> predicate, List<ISemantic> collector, SemanticType type);
	bool Scan(Func<ISemantic, bool> predicate, List<ISemantic> collector)
		=> Scan(predicate, collector, SemanticType.All);
}
