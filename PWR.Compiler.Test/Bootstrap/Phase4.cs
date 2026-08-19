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
		_tempFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pwr");
		Directory.CreateDirectory(_tempFolder);
		var filename = Directory.EnumerateFiles(".\\Code", "pwr*.pwrfl").OrderByDescending(f => f).First();
		var memManager = Directory.EnumerateFiles(".\\Code", "mem*.pwrfl").OrderByDescending(f => f).First();
		string[] inputs = [filename, memManager];
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

	private void RunTest(string code, string expected)
	{
		var options = new CompileOptions(
			[CodeSource.FromText(code)],
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
		process.WaitForExit();
		Assert.That(process.ExitCode, Is.EqualTo(0));
		var result = process.StandardOutput.ReadToEnd();
		Assert.That(result, Is.EqualTo(expected));
	}

	private void RunError(string code, string expected)
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

	// Default Parameters

	[Test]
	public void DefaultParams() => RunTest("""
		def addOffset(x: int, y: int = 10): int
			return x + y
		end

		print addOffset(5).ToString()     ; use default value
		print addOffset(5, 20).ToString() ; use explicit value
		""",
		"""
		15
		25

		""");

	[Test]
	public void NamedArguments() => RunTest("""
		def sub(x: int, y: int): int
			return x - y
		end

		print sub(x: 10, y: 3).ToString()
		print sub(y: 3, x: 10).ToString()
		print sub(10, y: 3).ToString()
		""",
		"""
		7
		7
		7

		""");

	[Test]
	public void NonConstantDefault() => RunError("""
		def bar(): int
			return 5
		end

		def foo(x: int = bar()): int
			return x
		end

		print foo().ToString()
		""",
		"must be a constant");

	[Test]
	public void NonDefaultAfterDefault() => RunError("""
		def foo(x: int = 5, y: int): int
			return x + y
		end

		print foo(1, 2).ToString()
		""",
		"cannot follow a defaulted parameter");

	[Test]
	public void PositionalAfterNamed() => RunError("""
		def foo(x: int, y: int): int
			return x + y
		end

		print foo(x: 5, 20).ToString()
		""",
		"positional argument cannot follow a named argument");

	[Test]
	public void ConstantFoldedDefault() => RunTest("""
		def foo(x: int = 3 + 2): int
			return x
		end

		print foo().ToString()
		""",
		"""
		5

		""");

		""");
}
