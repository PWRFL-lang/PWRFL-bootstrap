using System.Linq;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public abstract class Declaration(Position pos) : Node(pos), ISemanticNode
{
	public ISemantic? Semantic { get; set; }
	public Annotation[] Annotations { get; init; } = [];
	public bool HasAnnotation(string name) => Annotations.Any(a => a.Call.Target.ToString() == name);
	public Annotation? GetAnnotation(string name) => Annotations.FirstOrDefault(a => a.Call.Target.ToString() == name);
}
