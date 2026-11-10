using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public class NamespaceType(Project project, string name) : IType
{
	private readonly Project _project = project;

	public string Name { get; } = name;

	ISemantic? IType.GetMember(string name)
		=> _project.TypesIn(Name).FirstOrDefault(t => t.Name == name)
			?? (_project.IsNamespace($"{Name}.{name}") ? new NamespaceRef(_project, $"{Name}.{name}") : null);

	public override string ToString() => Name;
}
