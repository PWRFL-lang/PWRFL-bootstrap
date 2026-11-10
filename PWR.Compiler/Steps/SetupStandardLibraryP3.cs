using System;
using System.Collections.Generic;
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
			// ensure that the Console and Memory modules are loaded, so that "print" and "new" will work correctly
			// TODO: remove the Console hack once macros are implemented
			stdlib.Types.First(t => t.Name == "Console").GetMember("");
			stdlib.Types.First(t => t.Name == "Memory").GetMember("");
		}
		if (imports != null) {
			foreach (var imp in imports) {
				tree.Imports.Add(Import(imp));
			}
		}
		CheckImportsAgree(tree.Imports);
	}

	private static void CheckImportsAgree(List<ExternalLibrary> imports)
	{
		foreach (var library in imports) {
			foreach (var (name, id) in library.Dependencies) {
				var actual = imports.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
				if (actual != null && actual.Id != id) {
					throw new InvalidOperationException(
						$"Library '{library.Name}' was built against a different version of '{name}' than the one being imported. Rebuild '{library.Name}'.");
				}
			}
		}
	}

	private readonly Dictionary<string, ExternalLibrary> _loaded = new(StringComparer.OrdinalIgnoreCase);

	private ExternalLibrary Import(string filename)
	{
		var name = Path.GetFileNameWithoutExtension(filename);
		if (_loaded.TryGetValue(name, out var already)) {
			return already;
		}
		using var stream = LoadImport(filename, searchPath);
		using var ctx = new MetadataContext(stream);
		ctx.ImportResolver = (library, ns, type) => ResolveImportedType(name, library, ns, type);
		var result = new ExternalLibrary(ctx, name);
		_loaded.Add(name, result);
		return result;
	}

	private IType ResolveImportedType(string requester, string library, string? ns, string typeName)
	{
		var target = Import(library + ".dll");
		var expected = _loaded[requester].Dependencies.FirstOrDefault(d => string.Equals(d.Name, library, StringComparison.OrdinalIgnoreCase));
		if (expected.Name != null && expected.Id != target.Id) {
			throw new InvalidOperationException(
				$"Library '{requester}' was built against a different version of '{library}' than the one being imported. Rebuild '{requester}'.");
		}
		return target.Types.Cast<ExternalType>().FirstOrDefault(t => t.Name == typeName && t.Namespace == ns)
			?? throw new Exception($"Library '{library}' has no type '{ns}.{typeName}', which '{requester}' refers to.");
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
