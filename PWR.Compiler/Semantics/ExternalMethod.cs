using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

internal class ExternalMethod : ISemantic, IFunction
{
	public ExternalMethod(string name, IType[] sig, string[] names, IType owner)
	{
		Name = name;
		Type = sig[^1];
		Parent = owner;
		Debug.Assert(sig.Length == names.Length + 1);
		Args = [.. names.Zip(sig, KeyValuePair.Create)
			.Select(p => new ParameterDeclaration(
				new Identifier(default, p.Key),
				new SimpleTypeReference(default, p.Value.Name) { Semantic = new TypeRef(p.Value) }))];
		ReturnType = new SimpleTypeReference(default, Type.Name) { Semantic = new TypeRef(Type) };
	}

	public string Name { get; }

	public string FullName => $"{Parent.Name}${Name}";

	public SemanticType SemanticType => SemanticType.Function | SemanticType.External;

	public IType Type { get; }

	public IType Parent { get; }

	public TypeReference? ReturnType { get; }

	public ParameterDeclaration[] Args { get; }
}
