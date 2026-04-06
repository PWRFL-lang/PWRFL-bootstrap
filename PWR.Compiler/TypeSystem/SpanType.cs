using System.Collections.Generic;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem;

public class SpanType(IType baseType) : IType, ICollectionType
{
	public IType BaseType { get; } = baseType;

	public string Name => BaseType.Name + " span";

	public IType MakeArray()
	{
		throw new System.NotImplementedException();
	}

	public IType MakeSpan()
	{
		throw new System.NotImplementedException();
	}

	private readonly Dictionary<string, ISemantic> _members = new() {
		{ "Length",
			new MagicProperty(
				"Length",
				"span$Length",
				new SimpleTypeReference(default, "int") { Semantic = new TypeRef(Types.Int32) }
			)
		},
		{ "ToPtr",
			new MagicFunction(
				"ToPtr",
				"span$ToPtr",
				new SimpleTypeReference(default, "ptr") { Semantic = new TypeRef(Types.Ptr) }
			)
		}
	};

	ISemantic? IType.GetMember(string name)
	{
		_members.TryGetValue(name, out var result);
		return result;
	}
}
