using System.IO;

namespace PWR.Compiler;

public enum CompileType
{
	Jit,
	File
}

public enum ProjectType
{
	Library,
	Executable
}

public abstract class CodeSource
{
	public abstract string Filename { get; }
	public abstract string Code { get; }

	public static CodeSource FromText(string text) => new TextCodeSource(text);

	public static CodeSource FromFile(string filename) => new FileCodeSource(filename);

	private class TextCodeSource(string text, string filename = "code") : CodeSource
	{
		public override string Filename => filename;

		public override string Code => text;
	}

	private class FileCodeSource(string filename) : CodeSource
	{
		public override string Filename => filename;

		public override string Code => File.ReadAllText(filename);
	}
}

public record CompileOptions(
	CodeSource[] Files,
	string OutputFilename,
	CompileType CompileType = CompileType.File,
	ProjectType ProjectType = ProjectType.Executable,
	bool NoStdLib = false);
