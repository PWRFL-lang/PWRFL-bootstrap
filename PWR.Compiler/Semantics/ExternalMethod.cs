using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Semantics;

internal class ExternalMethod : ISemantic, IMethod, IMemberSemantic
{
	public ExternalMethod(string name, IType[] sig, string[] names, IType owner, bool hasSelf, FunctionFlags flags = FunctionFlags.None, int slot = -1, bool isConstructor = false)
	{
		_isConstructor = isConstructor;
		Flags = flags;
		Slot = slot;
		Name = name;
		Type = isConstructor ? owner : sig[^1];
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

	public string FullName => $"{Parent.FullName}${Name}";
	public FunctionFlags Flags { get; }
	IType IMethod.ParentType => Parent;
	IType IMemberSemantic.ParentType => Parent;
	public int Slot { get; }

	private readonly bool _hasSelf;
	private readonly bool _isConstructor;

	public SemanticType SemanticType => _isConstructor
		? SemanticType.Function | SemanticType.External | SemanticType.Constructor
		: _hasSelf
			? SemanticType.Function | SemanticType.External | SemanticType.HasSelf
			: SemanticType.Function | SemanticType.External;

	public IType Type { get; }

	public IType Parent { get; }
	public bool HasSelf => _hasSelf;

	public TypeReference? ReturnType { get; }

	public ParameterDeclaration[] Args { get; }
}
