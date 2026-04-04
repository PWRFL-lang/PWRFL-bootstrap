using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;

namespace PWR.Compiler.Steps;

public class SetupStandardLibrary : ICompileStep
{
	public Project Run(Project tree)
	{
		var p1 = new ParameterDeclaration(new(default, "value"), new SimpleTypeReference(default, "string") { Semantic = new TypeRef(Types.String)});
		p1.Semantic = new ParamDef(p1, 0);
		tree.Add(new MagicFunction("print", "$print", new SimpleTypeReference(default, "int"), p1));

		var p2 = new ParameterDeclaration(new(default, "value"), new SimpleTypeReference(default, "ordinal"));
		p2.Semantic = new ParamDef(p2, 0);
		tree.Add(new MagicFunction("ord", "ord", new SimpleTypeReference(default, "ordinal"), p2));

		var p3 = new ParameterDeclaration(new(default, "start"), new SimpleTypeReference(default, "ordinal"));
		p3.Semantic = new ParamDef(p3, 0);
		var p4 = new ParameterDeclaration(new(default, "end"), new SimpleTypeReference(default, "ordinal"));
		p4.Semantic = new ParamDef(p4, 1);
		var p5 = new ParameterDeclaration(new(default, "step"), new SimpleTypeReference(default, "ordinal"), new IntegerLiteralExpression(default, 1));
		p5.Semantic = new ParamDef(p5, 2);
		tree.Add(new MagicFunction("range", "range", new SequenceTypeReference(new SimpleTypeReference(default, "ordinal")), p3, p4, p5));

		var p6 = new ParameterDeclaration(new(default, "value"), new SimpleTypeReference(default, "string") { Semantic = new TypeRef(Types.String) });
		p6.Semantic = new ParamDef(p6, 0);
		tree.Add(new MagicFunction("StrToPtr", "StrToPtr", new SimpleTypeReference(default, "ptr") { Semantic = new TypeRef(Types.Ptr) }, p6));

		return tree;
	}
}
