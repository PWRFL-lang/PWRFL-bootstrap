using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

[Flags]
public enum FunctionFlags
{
	None = 0,
	Virtual = 1,
	Abstract = 2,
	Static = 4,
	TypeScope = 8,
}

public class FunctionDeclaration(Position pos, FunctionFlags flags, Identifier name, ParameterDeclaration[] parameters, TypeReference? returnType, Statement[] body)
	: Declaration(pos), IScope
{
	public FunctionDeclaration(Position pos, FunctionFlags flags, Identifier name, List<ParameterDeclaration> parameters, TypeReference? returnType, List<Statement> body)
		: this(pos, flags, name, parameters.ToArray(), returnType, [.. body])
	{ }

	public FunctionFlags Flags { get; } = flags;
	public Identifier Name { get; } = name;
	public ParameterDeclaration[] Parameters { get; } = [.. parameters];
	public TypeReference? ReturnType { get; } = returnType;
	public Statement[] Body { get; } = [.. body];

	public FunctionDeclaration With(Identifier name, ParameterDeclaration[] parameters, TypeReference? returnType, Statement[] body)
		=> new(Position, Flags, name, parameters, returnType, body) { SymbolTable = SymbolTable, Semantic = Semantic, IsConstructor = IsConstructor };

	public override NodeType Type => NodeType.FunctionDeclaration;

	public override void Accept(IVisitor visitor) => visitor.VisitFunctionDeclaration(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitFunctionDeclaration(this);

	internal List<ISemantic> SymbolTable { get; init; } = [];
	public bool IsConstructor { get; init; }

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
