using System.Collections.Generic;
using System.Runtime.InteropServices;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.Steps;

internal class BindNamespaces(string name) : VisitorCompileStep
{
	private readonly string _projectName = name;
	private Dictionary<string, List<ISemantic>> _namespaces = null!;

	public override void VisitProject(Project node)
	{
		_namespaces = [];
		base.VisitProject(node);
	}

	public override void VisitCodeFile(CodeFile node)
	{
		if (node.Namespace == null) {
			node.Namespace = _projectName;
		} else if (!(node.Namespace == _projectName || node.Namespace.StartsWith(_projectName + '.'))) {
			throw new CompileError(node, $"Namespace must be or begin with project name: '{_projectName}'");
		}
		ref var table = ref CollectionsMarshal.GetValueRefOrAddDefault(_namespaces, node.Namespace, out _);
		table ??= [];
		node.SymbolTable = table;
	}
}