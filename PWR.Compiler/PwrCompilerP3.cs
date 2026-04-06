using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Parser;

namespace PWR.Compiler;

/*
 * New compiler frontend architecture for phase 3.
 * If we're compiling from files now, simple string input won't cut it.
 * And options are becoming more complex, so let's move them to a
 * CompileOptions class.
 */
public class PwrCompilerP3
{
	public CompileResult Compile(CompileOptions options)
	{
		var tree = Parse(options.Files);
		var result = new CompilePipelineP3(options).Run(tree);
		return result;
	}

	private Project Parse(CodeSource[] files)
	{
		if (files.Length == 1) {
			return new Project(ParseFile(files[0]));
		}
		return new Project(files.AsParallel().Select(ParseFile).ToArray());
	}

	private static CodeFile ParseFile(CodeSource file)
	{
		var parser = new PwrParserP3(file.Code, file.Filename);
		return parser.Parse();
	}
}
