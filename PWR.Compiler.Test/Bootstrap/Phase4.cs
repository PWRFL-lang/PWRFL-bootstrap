using System.Diagnostics;

namespace PWR.Compiler.Test.Bootstrap;

/*
 * Compiler bootstrapping phase 4: Dynamic types.
 * Extend existing dynamic arrays with realloc behavior.
 * Implement interfaces and classes.
 */
internal class Phase4
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
		var memManager = Directory.EnumerateFiles(".\\Code", "mem*.pwrfl").OrderByDescending(f => f).First();
		string[] inputs = [filename, memManager];
		var latestChange = inputs.Max(File.GetLastWriteTime);
		var runtimeFilename = Path.Combine(_tempFolder, "pwr.dll");
		if (latestChange > File.GetLastWriteTime(runtimeFilename))
		{
			var options = new CompileOptions(
				[CodeSource.FromFile(filename), CodeSource.FromFile(memManager)],
				runtimeFilename,
				[],
				ProjectType: ProjectType.Library,
				NoStdLib: true
			);
			var result = _compiler.Compile(options);
			Assert.That(result is BuildCompileResult);
			_runtime = ((BuildCompileResult)result).Filename;
		}
	}

	private void RunTest(string code, string expected)
	{
		var options = new CompileOptions(
			[CodeSource.FromText(code)],
			Path.Combine(_tempFolder, "test.exe"),
			[_tempFolder]
		);
		var cr = _compiler.Compile(options);
		if (cr is not BuildCompileResult { Filename: { } filename })
		{
			Assert.Fail("Build failed: " + ((ErrorCompileResult)cr).Error);
			// this won't be hit because Assert.Fail throws, but the C# compiler requires it
			// for definite assignment analysis of `filename` below
			throw new UnreachableException();
		}

		var process = Process.Start(new ProcessStartInfo(filename)
		{ RedirectStandardOutput = true, UseShellExecute = false })!;
		process.WaitForExit();
		Assert.That(process.ExitCode, Is.EqualTo(0));
		var result = process.StandardOutput.ReadToEnd();
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public void ArrayAlloc() => RunTest("""
		var a = new int[5]
		a[0] = 10
		a[1] = 20
		a[2] = 30
		print a[0].ToString()
		print a.Length.ToString()
		""",
		"""
		10
		5

		""");

	[Test]
	public void ArrayResize() => RunTest("""
		var a = new int[3]
		a[0] = 1
		a[1] = 2
		a[2] = 3
		a.Resize(6)
		a[3] = 4
		a[4] = 5
		a[5] = 6
		print a[3].ToString()
		print a.Length.ToString()
		""",
		"""
		4
		6

		""");

	[Test]
	public void ArrayResizePreservesValues() => RunTest("""
		var a = new int[3]
		a[0] = 42
		a.Resize(10)
		print a[0].ToString()
		""",
		"""
		42

		""");


	[Test]
	public void ArrayResizeShrink() => RunTest("""
		var a = new int[10]
		a[0] = 99
		a.Resize(3)
		print a[0].ToString()
		print a.Length.ToString()
		""",
		"""
		99
		3

		""");
}
