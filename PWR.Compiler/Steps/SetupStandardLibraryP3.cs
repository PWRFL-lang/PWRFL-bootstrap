using System.IO;
using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.External;

namespace PWR.Compiler.Steps;

public class SetupStandardLibraryP3(string[]? imports, bool skipStdLib, string[] searchPath) : ICompileStep
{
	public Project Run(Project tree)
	{
		SetupStdLib(tree);
		SetupImports(tree);
		return tree;
	}

	private static void SetupStdLib(Project tree)
	{
		var p1 = new ParameterDeclaration(new(default, "value"), new SimpleTypeReference(default, "string").WithType(Types.String));
		p1.Semantic = new ParamDef(p1, 0);
		tree.Add(new MagicFunction("print", "$print", new SimpleTypeReference(default, "int").WithType(Types.Int32), p1));

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

		var p6 = new ParameterDeclaration(new(default, "value"), new SimpleTypeReference(default, "string").WithType(Types.String));
		p6.Semantic = new ParamDef(p6, 0);
		tree.Add(new MagicFunction("StrToPtr", "StrToPtr", new SimpleTypeReference(default, "ptr").WithType(Types.Ptr), p6));
	}

	private void SetupImports(Project tree)
	{
		if (!skipStdLib) {
			var stdlib = Import("pwr.dll");
			tree.Imports.Add(stdlib);
			// ensure that the Console type is loaded, so that "print" will work correctly
			// TODO: Fix this once macros are implemented
			stdlib.Types.First(t => t.Name == "Console").GetMember("");
		}
		if (imports != null) {
			foreach (var imp in imports) {
				tree.Imports.Add(Import(imp));
			}
		}
	}

	private ExternalLibrary Import(string filename)
	{
		var stream = LoadImport(filename, searchPath);
		using var ctx = new MetadataContext(stream);
		var name = Path.GetFileNameWithoutExtension(filename);
		return new ExternalLibrary(ctx, name);
	}

	private static FileStream LoadImport(string filename, string[] searchPath)
	{
		if (File.Exists(filename)) {
			return File.OpenRead(filename);
		}
		foreach (var path in searchPath) {
			var p2 = Path.Combine(path, filename);
			if (File.Exists(p2)) {
				return File.OpenRead(p2);
			}
		}

		throw new FileNotFoundException($"Unable to load import library '{filename}'.");
	}
}
