using System;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.External;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Steps;

internal class BindParentTypes : ScopeSensitiveCompileStep
{
	public override void VisitClassDeclaration(ClassDeclaration node)
	{
		base.VisitClassDeclaration(node);
		for (int i = 0; i < node.Inherited.Length; ++i) {
			var name = QualifiedName(node.Inherited[i]);
			var types = LookupType(name);
			switch (types.Count) {
				case 0: throw new CompileError(node.Inherited[i], $"Type '{name}' not found.");
				case 1:
					BindParentType(node, types[0], i == 0);
					break;
				default: throw new CompileError(node.Inherited[i], $"Ambiguous type name '{name}' could be any of: {string.Join(", ", types.Select(t => t.FullName))}.");
			}
		}
	}

	// The name as it was written, with the namespace if it has one: `shapes.Square`.
	private static string QualifiedName(Expression name)
		=> name is MemberIdentifier member ? $"{QualifiedName(member.ParentExpr)}.{member.Name}" : ((Identifier)name).Name;

	private void BindParentType(ClassDeclaration node, ISemantic semantic, bool isFirst)
	{
		if (isFirst) {
			var type = (InternalClass)node.Semantic!.Type;
			type.BaseType = (IClassType)semantic.Type;
			// Every later walk up the base chain (member lookup, assignability, VMT building)
			// assumes it terminates, so reject a cycle as soon as the link that closes it is set.
			for (var current = type.BaseType; current != null; current = current.BaseType) {
				if (current == type) {
					throw new CompileError(node, $"Class '{type.Name}' cannot inherit from itself, directly or indirectly.");
				}
			}
		} else {
			throw new CompileError(node, "Multiple inheritance is not supported");
		}
	}
}
