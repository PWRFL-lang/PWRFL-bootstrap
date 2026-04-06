using System;
using System.Collections.Generic;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

public class AddTypeConversions : TransformerCompileStep
{
	public override T[]? Visit<T>(T[]? list)
	{
		if (list == null || list.Length == 0) {
			return list;
		}
		var result = new List<T>();
		foreach (var node in list) {
			// .NET JIT should turn this into a constant and generate different code paths for expressions
			if (typeof(T).IsAssignableTo(typeof(Expression))) {
				var value = VisitExpression(node as Expression) as T;
				if (value != null) {
					result.Add(value);
				}
			} else { 
				var value = (T?)node.Accept(this);
				if (value != null) {
					result.Add(value);
				}
			}
		}
		if (result.Count == 0) {
			return null;
		}
		if (result.Count == list.Length && result.SequenceEqual(list)) {
			return list;
		}
		return [..result];
	}

	protected override Expression? VisitExpression(Expression? node)
	{
		var result = base.VisitExpression(node);
		var castType = result?.GetAnnotation("cast") as IType;
		return castType == null
			? result
			: new CastExpression(
				result!, new SimpleTypeReference(default, castType.Name) { Semantic = new TypeRef(castType) }
			) { Semantic = new TypeRef(castType) };
	}
}
