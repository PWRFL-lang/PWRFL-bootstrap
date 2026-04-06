using System.Diagnostics;

namespace PWR.Compiler.Test.Bootstrap;

/*
 * Compiler bootstrapping phase 3: Building a rudimentary standard library.
 * Build pwr.dll as its own library, with no libc dependencies.  Compiler should
 * link against it.  It should have self-describing metadata so the compiler
 * knows what it's looking at without the need for external sources such as a
 * .h or .lib file.
 */
internal class Phase3
{
	private PwrCompilerP3 _compiler = null!;
	private string _tempFolder = null!;
	private string _runtime = null!;

	[OneTimeSetUp]
	public void Setup()
	{
		_compiler = new PwrCompilerP3();
		_tempFolder = Path.Combine(Path.GetTempPath(), "pwr");
		Directory.CreateDirectory(_tempFolder);
		var filename = Directory.EnumerateFiles(".\\Code", "pwr*.pwrfl").OrderByDescending(f => f).First();
		var options = new CompileOptions(
			[CodeSource.FromFile(filename)],
			Path.Combine(_tempFolder, "pwr.dll"), 
			ProjectType: ProjectType.Library,
			NoStdLib: true
		);
		var result = _compiler.Compile(options);
		Assert.That(result is BuildCompileResult);
		_runtime = ((BuildCompileResult)result).Filename;
	}

	private void RunTest(string code, string expected)
	{
		var options = new CompileOptions(
			[CodeSource.FromText(code)],
			Path.Combine(_tempFolder, "test.exe")
		);
		var cr = _compiler.Compile(options);
		if (cr is not BuildCompileResult { Filename: { } filename }) {
			Assert.Fail("Build failed: " + ((ErrorCompileResult)cr).Error);
			// this won't be hit because Assert.Fail throws, but the C# compiler requires it
			// for definite assignment analysis of `filename` below
			throw new UnreachableException();
		}

		var process = Process.Start(new ProcessStartInfo(filename + ".exe")
			{ RedirectStandardOutput = true, UseShellExecute = false })!;
		process.WaitForExit();
		Assert.That(process.ExitCode, Is.EqualTo(0));
		var result = process.StandardOutput.ReadToEnd();
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public void HelloWorld() => RunTest("Console.PrintLn(\"Hello, World!\")", "Hello, World!\r\n");
}
