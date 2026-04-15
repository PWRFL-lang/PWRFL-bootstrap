using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem.External;

internal class ExternalLibrary(MetadataContext context, string name) : IScope
{
	public string Name { get; } = name;
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
		var assoc = typ.AssociatedTypeRef == 0 ? null : Signatures.ReadField(context.GetBlob(typ.AssociatedTypeRef), context);
		return typ.Type switch {
			TypeOfType.Module => new ExternalModule(ns, name, parent, assoc, context, idx),
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

	public bool Scan(Func<ISemantic, bool> predicate, List<ISemantic> collector, SemanticType type)
	{
		if ((type & SemanticType.Type) == 0) {
			return false;
		}
		var result = Types.Select(t => new TypeRef(t)).FirstOrDefault(predicate);
		if (result != null) {
			collector.Add(result);
			return true;
		}
		return false;
	}
}
