using System;
using System.Collections.Generic;
using System.Linq;

using LLVMSharp.Interop;

using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem.External;
using PWR.Compiler.Helpers;

namespace PWR.Compiler.Ast;

public class Project(params CodeFile[] files): Node(default), IScope
{
	public CodeFile[] Files { get; } = files;
	public override NodeType NodeType => NodeType.Project;

	internal LLVMValueRef EntryPoint { get; set; }
	internal MetadataHeader MetadataHeader { get; set; }
	internal byte[] Metadata { get; set; } = null!;
	internal byte[] BlobData { get; set; } = null!;
	internal List<MetaclassFixup> MetaclassFixups { get; set; } = [];
	internal Guid LibraryId { get; set; }
	internal List<ExternalLibrary> Imports { get; init; } = [];

	public Project With(CodeFile[] files) => new(files) { _globals = this._globals, Imports = this.Imports };

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
		var found = false;
		foreach (var imp in Imports) {
			var iFound = imp.Lookup(name, collector, type);
			found |= iFound;
		}

		if (!found && type.HasFlag(SemanticType.Namespace) && IsNamespace(name)) {
			collector.Add(new NamespaceRef(this, name));
			found = true;
		}
		return found;
	}

	private IEnumerable<string> Namespaces
		=> Files.Select(f => f.Namespace)
		.Concat(Imports
			.SelectMany(i => i.Types)
			.OfType<ExternalType>()
			.Select(t => t.Namespace))
		.WhereNotNull()
		.Distinct();

	internal bool IsNamespace(string name) => Namespaces.Any(ns => ns == name || ns.StartsWith(name + '.'));

	internal IEnumerable<ISemantic> TypesIn(string ns)
		=> Files.Where(f => f.Namespace == ns)
			.SelectMany(f => f.SymbolTable ?? [])
			.Where(s => s.SemanticType.HasFlag(SemanticType.Type))
			.Distinct()
			.Concat(Imports
				.SelectMany(i => i.Types)
				.OfType<ExternalType>()
				.Where(t => t.Namespace == ns)
				.Select(t => (ISemantic)new TypeRef(t)));

	public bool Scan(Func<ISemantic, bool> predicate, List<ISemantic> collector, SemanticType type)
	{
		var count = collector.Count;
		collector.AddRange(_globals.Values.Where(g => ((uint)g.SemanticType & (uint)type) != 0 && predicate(g)));
		foreach (var imp in Imports) {
			imp.Scan(predicate, collector, type);
		}
		return collector.Count > count;
	}
}
