using System.Collections.Generic;

using LLVMSharp.Interop;

using PWR.Compiler.Semantics;

namespace PWR.Compiler.Ast;

public class Project(params CodeFile[] files): Node(default), IScope
{
	public CodeFile[] Files { get; } = files;
	public override NodeType Type => NodeType.Project;

	internal LLVMValueRef EntryPoint { get; set; }

	public Project With(CodeFile[] files) => new(files) { _globals = this._globals };

	public override void Accept(IVisitor visitor) => visitor.VisitProject(this);
	public override Node? Accept(ITransformer visitor) => visitor.VisitProject(this);

	private Dictionary<string, ISemantic> _globals = [];

	public void Add(ISemantic semantic) => _globals.Add(semantic.Name, semantic);

	public bool Lookup(string name, List<ISemantic> collector, SemanticType type)
	{
		if (_globals.TryGetValue(name, out var result) && ((uint)result.SemanticType & (uint)type) != 0) {
			collector.Add(result);
			return true;
		}
		return false;
	}
}
