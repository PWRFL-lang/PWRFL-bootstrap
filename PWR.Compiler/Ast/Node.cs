using System.Collections.Generic;
using System.Linq;

namespace PWR.Compiler.Ast;

public abstract class Node(Position position)
{
	public Position Position { get; } = position;
	public Node Parent { get; set; } = null!;
	public abstract NodeType Type { get; }
	public abstract void Accept(IVisitor visitor);
	public abstract Node? Accept(ITransformer visitor);

	public IEnumerable<T> GetAncestors<T>() where T: Node
	{
		var parent = this.Parent;
		while (parent != null) {
			if (parent is T result) {
				yield return result;
			}
			parent = parent.Parent;
		}
	}

	public T? GetAncestor<T>() where T : Node => GetAncestors<T>().FirstOrDefault();
}
