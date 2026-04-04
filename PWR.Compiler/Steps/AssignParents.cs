using System.Collections.Generic;

using PWR.Compiler.Ast;

namespace PWR.Compiler.Steps;

public class AssignParents : VisitorCompileStep
{
	private readonly Stack<Node> _parents = [];

	public override void Visit(Node? node)
	{
		if (node != null) {
			_parents.Push(node);
			try {
				base.Visit(node);
			} finally {
				_parents.Pop();
				if (_parents.Count > 0) {
					node.Parent = _parents.Peek();
				}
			}
		}
	}

	public override void Visit<T>(T[]? list)
	{
		if (list != null) {
			foreach (var node in list) {
				Visit(node);
			}
		}
	}
}
