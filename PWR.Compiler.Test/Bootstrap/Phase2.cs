using System.Diagnostics;

namespace PWR.Compiler.Test.Bootstrap;

/*
 * Compiler bootstrapping phase 2: Proof of concept for FFI and binary building.
 * Build executable code with LLVM, save to file, link to EXE, run out-of-process
 * and write to STDOUT with standard libc functions, and read the process's STDOUT.
 */

public class Phase2
{
	private PwrCompiler _compiler = null!;
	private string _tempFolder = null!;

	[OneTimeSetUp]
	public void Setup()
	{
		_compiler = new PwrCompiler();
		_tempFolder = Path.Combine(Path.GetTempPath(), "pwr");
		Directory.CreateDirectory(_tempFolder);
	}

	private void RunTest(string code, string expected)
	{
		var filename = Path.Combine(_tempFolder, "test");
		_compiler.CompileToFile(code, filename);

		var process = Process.Start(new ProcessStartInfo(filename + ".exe")
			{ RedirectStandardOutput = true, UseShellExecute = false })!;
		process.WaitForExit();
		Assert.That(process.ExitCode, Is.EqualTo(0));
		var result = process.StandardOutput.ReadToEnd();
		Assert.That(result, Is.EqualTo(expected));
	}

	// working: 4/03/2026
	[Test]
	[Platform(Include = "Win")]
	public void FfiHelloWorld() => RunTest(
		"""
		@ExternalLibraryName("msvcrt")
		module Runtime
			def abstract puts(value: ptr): int

			def WriteLn(value: string)
				puts(StrToPtr(value))
			end
		end

		Runtime.WriteLn("Hello, World!")
		""",
		"Hello, World!\r\n"
	);
}
