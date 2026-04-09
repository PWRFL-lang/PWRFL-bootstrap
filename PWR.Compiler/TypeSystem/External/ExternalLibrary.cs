using System;
using System.Collections.Generic;
using System.Linq;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem.External;

internal class ExternalLibrary(MetadataContext context) : IScope
{
	public IType[] Types { get; } = ReadContext(context);

	private static IType[] ReadContext(MetadataContext context)
	{
		var arr = new IType[context.TypeDefinitionTable.Count];
		for (int i = 0; i < context.TypeDefinitionTable.Count; i++) {
			var typ = context.TypeDefinitionTable[i];
			arr[i] = ReadType(typ, context, i);
		}
		return arr;
	}

	private static IType ReadType(TypeDefinition typ, MetadataContext context, int idx)
	{
		var ns = typ.GetNamespace(context);
		var name = typ.GetName(context);
		var parent = typ.GetParentType(context);
		return typ.Type switch {
			TypeOfType.Module => new ExternalModule(ns, name, parent, context, idx),
			_ => throw new NotImplementedException()
		};
	}

	public void Add(ISemantic semantic) => throw new NotImplementedException();

	public bool Lookup(string name, List<ISemantic> collector, SemanticType type)
	{
		if ((type & SemanticType.Type) == 0) {
			return false;
		}
		var result = Types.FirstOrDefault(t => t.Name == name);
		if (result != null) {
			collector.Add(new TypeRef(result));
			return true;
		}
		return false;
	}
}
