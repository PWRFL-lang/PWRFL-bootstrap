using System;

using PWR.Compiler.Ast;
using PWR.Compiler.Parser;

namespace PWR.Compiler;

public class PwrCompiler(IntPtr printf = 0)
{
	private readonly IntPtr _printf = printf;

	public Action? CompileToMemory(string code)
	{
		var tree = Parse(code);
		var result = new CompilePipeline(CompileType.Jit, _printf).Run(tree);
		return result;
	}

	private static Project Parse(string code)
	{
		var parser = new PwrParser(code, "code");
		var file = parser.Parse();
		return new Project(file);
	}

	public void CompileToFile(string code, string filename)
	{
		var tree = Parse(code);
		new CompilePipeline(CompileType.File, _printf).Compile(tree, filename);
	}
}
