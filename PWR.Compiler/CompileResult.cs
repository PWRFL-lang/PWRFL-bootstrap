using System;
using System.Diagnostics;

namespace PWR.Compiler;

public abstract class CompileResult(Stopwatch sw)
{
	private readonly TimeSpan _time = sw.Elapsed;
}

public class JitCompileResult(Action? entryPoint, Stopwatch sw) : CompileResult(sw)
{
	public Action? EntryPoint { get; } = entryPoint;
}

public class BuildCompileResult(string filename, Stopwatch sw) : CompileResult(sw)
{
	public string Filename { get; } = filename;
}

public class ErrorCompileResult(string error, Stopwatch sw) : CompileResult(sw)
{
	public string Error { get; } = error;
}