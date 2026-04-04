using System.Collections.Generic;

namespace PWR.Compiler.Semantics;

public interface IScope
{
	void Add(ISemantic semantic);
	bool Lookup(string name, List<ISemantic> collector, SemanticType type);
	bool Lookup(string name, List<ISemantic> collector)
		=> Lookup(name, collector, SemanticType.All);
}
