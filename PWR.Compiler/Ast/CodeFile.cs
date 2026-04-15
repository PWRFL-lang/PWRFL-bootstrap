using System;
using System.Collections.Generic;
using System.Linq;

using LLVMSharp.Interop;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public class CodeFile(string filename, Declaration[] decls, Statement[] body) : Node(new Position(filename, 0, 0)), IScope
{
	public CodeFile(string filename, List<Declaration> decls, List<Statement> body) : this(filename, decls.ToArray(), [..body])
	{ }

	public Declaration[] Decls { get; } = decls;
	public Statement[] Body { get; } = body;
	public override NodeType Type => NodeType.CodeFile;

	public CodeFile With(Declaration[] decls, Statement[] body)
		=> new(this.Position.File, decls, body) { SymbolTable =  this.SymbolTable };

	internal LLVMValueRef EntryPoint { get; set; }

	internal List<ISemantic> SymbolTable { get; init; } = [];

	public override void Accept(IVisitor visitor) => visitor.VisitCodeFile(this);

	public override Node? Accept(ITransformer visitor) => visitor.VisitCodeFile(this);

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
