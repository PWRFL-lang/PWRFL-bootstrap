using PWR.Compiler.Ast;
using PWR.Compiler.TypeSystem;
using System.Collections.Generic;
using System.Linq;

namespace PWR.Compiler.Steps;

internal class CheckMembers : VisitorCompileStep
{

	public override void VisitClassDeclaration(ClassDeclaration node) => DoCheck(node);

	public override void VisitStructDeclaration(StructDeclaration node) => DoCheck(node);

	public override void VisitModuleDeclaration(ModuleDeclaration node) => DoCheck(node);

	private void DoCheck(TypeDeclaration node)
	{
		var dupe = node.Body
			.Select(d => d.Semantic!.Name)
			.GroupBy(n => n, (k, g) => KeyValuePair.Create(k, g.Count()))
			.Where(p => p.Value > 1)
			.FirstOrDefault();
		if (dupe.Key != null) {
			throw new CompileError(node, $"Type member '{dupe.Key}' is declared more than once.");
		}
	}
}
