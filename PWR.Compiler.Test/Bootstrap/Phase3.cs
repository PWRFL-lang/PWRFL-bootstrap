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

	[Test]
	public void HelloWorld() => RunTest("Console.PrintLn(\"Hello, World!\")", "Hello, World!\r\n");

	[Test]
	public void Structs() => RunTest("""
		struct Point
			X: int
			Y: int
		end

		var p = Point(1, 2)
		Console.Print((p.X + p.Y).ToString())
		""",
		"3");

	[Test]
	public void StructsMethods() => RunTest("""
		struct Point
			X: int
			Y: int

			def Sum(): int
				return X + Y
			end
		end

		var p = Point(1, 2)
		Console.Print(p.Sum().ToString())
		""",
		"3");

	[Test]
	public void StructsValueSemantics() => RunTest("""
		struct Point
			X: int
			Y: int
		end

		var p = Point(1, 2)
		var q = p
		q.X = 99
		Console.Print(p.X.ToString())
		""",
		"1");

	[Test]
	public void StructsPassing() => RunTest("""
		struct Point
			X: int
			Y: int
		end

		def Sum(p: Point): int
			return p.X + p.Y
		end

		Console.Print(Sum(Point(3, 4)).ToString())
		""",
		"7");

	[Test]
	public void StructsFields() => RunTest("""
		struct Point
			X: int
			Y: int
		end

		struct Rectangle
			TopLeft: Point
			BottomRight: Point

			def Width(): int
				return BottomRight.X - TopLeft.X
			end

			def Height(): int
				return BottomRight.Y - TopLeft.Y
			end

			def Area(): int
				return Width() * Height()
			end
		end

		var r = Rectangle(Point(0, 0), Point(10, 5))
		Console.Print(r.Area().ToString())
		""",
		"50");

	[Test]
	public void StructsReturning() => RunTest("""
		struct Point
			X: int
			Y: int
		end

		def Midpoint(a: Point, b: Point): Point
			return Point((a.X + b.X) // 2, (a.Y + b.Y) // 2)
		end

		var m = Midpoint(Point(0, 0), Point(10, 10))
		Console.Print(m.X.ToString())
		""",
		"5");

	[Test]
	public void Alloc() => RunTest("""
		var block = Memory.Alloc(64)
		; write something to it
		block[0] = 42
		; verify we can read it back
		print block[0].ToString()
		; free it
		Memory.Free(block)
		""",
		"42\r\n");

	[Test]
	public void AllocMulti() => RunTest("""
		let a = Memory.Alloc(16)
		let b = Memory.Alloc(16)
		let c = Memory.Alloc(16)
		; verify they don't overlap
		a[0] = 1
		b[0] = 2
		c[0] = 3
		print a[0].ToString()  ; 1
		print b[0].ToString()  ; 2
		print c[0].ToString()  ; 3
		Memory.Free(a)
		Memory.Free(b)
		Memory.Free(c)
		""",
		"""
		1
		2
		3

		""");

	[Test]
	public void AllocSizeClasses() => RunTest("""
		let small = Memory.Alloc(16)    ; size class 0
		let large = Memory.Alloc(256)   ; size class 15
		let huge = Memory.Alloc(512)    ; large block path
		small[0] = 1
		large[0] = 2
		huge[0] = 3
		print small[0].ToString()
		print large[0].ToString()
		print huge[0].ToString()
		Memory.Free(small)
		Memory.Free(large)
		Memory.Free(huge)
		""",
		"""
		1
		2
		3

		""");
		
}
