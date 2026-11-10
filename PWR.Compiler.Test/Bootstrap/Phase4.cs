using System.Diagnostics;

namespace PWR.Compiler.Test.Bootstrap;

/*
 * Compiler bootstrapping phase 4: Dynamic types.
 * Extend existing dynamic arrays with realloc behavior.
 * Implement classes.
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
		_tempFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pwr");
		Directory.CreateDirectory(_tempFolder);
		var filename = Directory.EnumerateFiles(".\\Code", "pwr*.pwrfl").OrderByDescending(f => f).First();
		var memManager = Directory.EnumerateFiles(".\\Code", "mem*.pwrfl").OrderByDescending(f => f).First();
		string[] inputs = [filename, memManager, typeof(PwrCompilerP3).Assembly.Location];
		var latestChange = inputs.Max(File.GetLastWriteTime);
		var runtimeFilename = Path.Combine(_tempFolder, "pwr.dll");
		if (latestChange > File.GetLastWriteTime(runtimeFilename)) {
			var options = new CompileOptions(
				[CodeSource.FromFile(filename), CodeSource.FromFile(memManager)],
				runtimeFilename,
				[],
				ProjectType: ProjectType.Library,
				NoStdLib: true
				//, EmitLlvmIr: true
			);
			var result = _compiler.Compile(options);
			Assert.That(result is BuildCompileResult);
			_runtime = ((BuildCompileResult)result).Filename;
		}
	}

	protected void RunTest(string code, string expected) => RunTestFiles([code], expected);

	protected void RunTestFiles(string[] sources, string expected)
	{
		var options = new CompileOptions(
			[.. sources.Select(CodeSource.FromText)],
			Path.Combine(_tempFolder, "test.exe"),
			[_tempFolder]
			//, EmitLlvmIr: true
		);
		var cr = _compiler.Compile(options);
		if (cr is not BuildCompileResult { Filename: { } filename }) {
			Assert.Fail("Build failed: " + ((ErrorCompileResult)cr).Error);
			// this won't be hit because Assert.Fail throws, but the C# compiler requires it
			// for definite assignment analysis of `filename` below
			throw new UnreachableException();
		}

		var process = Process.Start(new ProcessStartInfo(filename)
		{ RedirectStandardOutput = true, UseShellExecute = false })!;
		if (!process.WaitForExit(TimeSpan.FromSeconds(5))) {
			process.Kill();
			Assert.Fail("Timeout running test");
		}
		Assert.That(process.ExitCode, Is.EqualTo(0));
		var result = process.StandardOutput.ReadToEnd();
		Assert.That(result, Is.EqualTo(expected));
	}

	protected void RunError(string code, string expected)
	{
		var options = new CompileOptions(
			[CodeSource.FromText(code)],
			Path.Combine(_tempFolder, "test.exe"),
			[_tempFolder]
			//, EmitLlvmIr: true
		);
		var cr = _compiler.Compile(options);
		Assert.Multiple(() => {
			Assert.That(cr, Is.InstanceOf<ErrorCompileResult>(), "Expected a compile error, but the build succeeded.");
			Assert.That(((ErrorCompileResult)cr).Error, Does.Contain(expected));
		});
	}

	protected void RunParseError(string code, string expected)
	{
		var options = new CompileOptions([CodeSource.FromText(code)], Path.Combine(_tempFolder, "test.exe"), [_tempFolder]);
		var ex = Assert.Throws<PWR.Compiler.Parser.ParseError>(() => _compiler.Compile(options));
		Assert.That(ex!.Message, Does.Contain(expected));
	}

}
