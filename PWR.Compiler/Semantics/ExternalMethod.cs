using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

internal class ExternalMethod : ISemantic, IFunction
{
	public ExternalMethod(string name, IType[] sig, string[] names, IType owner, bool hasSelf)
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
		_hasSelf = hasSelf;
	}

	public string Name { get; }

	public string FullName => $"{Parent.Name}${Name}";

	private readonly bool _hasSelf;

	public SemanticType SemanticType => _hasSelf
		? SemanticType.Function | SemanticType.External | SemanticType.HasSelf
		: SemanticType.Function | SemanticType.External;

	public IType Type { get; }

	public IType Parent { get; }
	public bool HasSelf => _hasSelf;

	public TypeReference? ReturnType { get; }

	public ParameterDeclaration[] Args { get; }

}
