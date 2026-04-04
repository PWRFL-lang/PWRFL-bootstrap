using System.Collections.Generic;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public abstract class Expression(Position position) : Node(position), ISemanticNode
{
	public virtual bool IsLiteral => false;
	public ISemantic? Semantic { get; set; }

	private Dictionary<string, object>? _annotations;

	public void Annotate(string name, object value)
	{
		_annotations ??= [];
		_annotations.Add(name, value);
	}

	public object? GetAnnotation(string name)
		=> _annotations?.TryGetValue(name, out var result) != true ? null : result;
}
