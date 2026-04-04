using System;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

public class AddTypeConversions : TransformerCompileStep
{
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
