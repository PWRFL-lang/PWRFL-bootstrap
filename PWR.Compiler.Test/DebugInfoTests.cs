using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PWR.Compiler.Test;

internal class DebugInfoTests
{
	private static string Folder => ExternalClassTests.TempFolder;

	private const string Source = """
		struct Point
			X: int
			Y: int
		end

		class Animal
			Legs: int = 4

			constructor virtual Create()
				super Create()
			end

			def virtual Speak(): string
				return "..."
			end
		end

		class Dog extends Animal
			Name: string

			constructor override Create()
				super Create()
				Name = "rex"
			end

			def override Speak(): string
				return "woof"
			end
		end

		module Util
			Count: int = 3

			def Add(a: int, b: int): int
				let total = a + b
				return total
			end
		end

		let d = Dog.Create()
		let p = Point(1, 2)
		var arr = new int[4]
		arr[0] = Util.Add(p.X, p.Y)
		print d.Speak()
		print arr[0].ToString()
		""";

	private string _pdb = null!;
	private string _exe = null!;
	private string _symbols = null!;
	private string _types = null!;

	[OneTimeSetUp]
	public async Task BuildProgram()
	{
		Directory.CreateDirectory(Folder);
		ExternalClassTests.EnsureRuntime();
		_exe = Path.Combine(Folder, "dbgtest.exe");
		var result = new PwrCompilerP3().Compile(new CompileOptions(
			[CodeSource.FromText(Source)], _exe, [Folder], DebugInfo: true, EmitLlvmIr: true));
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
		_pdb = Path.ChangeExtension(_exe, ".pdb");
		Assert.That(File.Exists(_pdb), Is.True, "no PDB was written");
		_symbols = await PdbUtil("--symbols");
		_types = await PdbUtil("--types");
	}

	private async static Task<string> PdbUtil(string option, string? pdb = null)
	{
		var info = new ProcessStartInfo("llvm-pdbutil", $"dump {option} \"{pdb ?? Path.Combine(Folder, "dbgtest.pdb")}\"") {
			RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
		};
		using var process = Process.Start(info) ?? throw new InvalidOperationException("llvm-pdbutil didn't start");
		var output = process.StandardOutput.ReadToEndAsync();
		process.StandardError.ReadToEnd();
		process.WaitForExit();
		return await output;
	}

	// The record of one function, up to the next function or variable.
	private string Function(string name)
	{
		var match = Regex.Match(_symbols, $"S_GPROC32 \\[size = \\d+\\] `{Regex.Escape(name)}`(.*?)(?=S_GPROC32|S_GDATA32|S_LDATA32|\\z)", RegexOptions.Singleline);
		Assert.That(match.Success, Is.True, $"no function '{name}' in the PDB");
		return match.Value;
	}

	// The line of the source (counting from 1) that has the text. It's a mistake for it not to be there.
	private static int LineOf(string text)
	{
		var index = Source.Split('\n').ToList().FindIndex(l => l.Contains(text));
		return index >= 0 ? index + 1 : throw new ArgumentException($"The test source has no line containing '{text}'.", nameof(text));
	}

	[Test]
	public void ProgramWithClassesBuildsAndRunsWithDebugInfo()
	{
		var process = Process.Start(new ProcessStartInfo(_exe) { RedirectStandardOutput = true, UseShellExecute = false })!;
		Assert.Multiple(() => {
			Assert.That(process.WaitForExit(TimeSpan.FromSeconds(5)), Is.True);
			Assert.That(process.ExitCode, Is.EqualTo(0));
			Assert.That(process.StandardOutput.ReadToEnd().Replace("\r\n", "\n"), Is.EqualTo("woof\n3\n"));
		});
	}

	[Test]
	public void FunctionsAreDescribedWithTheirOwningType()
	{
		Assert.Multiple(() => {
			foreach (var name in new [] { "Animal::Create", "Animal::Speak", "Dog::Create", "Dog::Speak", "Util::Add", "main" }) {
				Assert.That(_symbols, Does.Contain($"`{name}`"), name);
			}
		});
	}

	[Test]
	public void FunctionSignaturesAreDescribed()
	{
		Assert.Multiple(() => {
			Assert.That(Function("Util::Add"), Does.Contain("(int (int, int))"));
			Assert.That(Function("Dog::Speak"), Does.Contain("(string* (Dog*))"));
			Assert.That(Function("Dog::Create"), Does.Contain("(void (Dog*))"));
			Assert.That(Function("main"), Does.Contain("(void ())"));
		});
	}

	[Test]
	public void ParametersAndLocalsAreDescribed()
	{
		var add = Function("Util::Add");
		Assert.Multiple(() => {
			Assert.That(add, Does.Match(@"S_LOCAL \[size = \d+\] `a`\s+type=0x\w+ \(int\), flags = param"));
			Assert.That(add, Does.Match(@"S_LOCAL \[size = \d+\] `b`\s+type=0x\w+ \(int\), flags = param"));
			Assert.That(add, Does.Match(@"S_LOCAL \[size = \d+\] `total`\s+type=0x\w+ \(int\), flags = none"));
			// A method's `self` is its first parameter, and is the class it's a method of.
			Assert.That(Function("Dog::Speak"), Does.Match(@"S_LOCAL \[size = \d+\] `self`\s+type=0x\w+ \(Dog\*\), flags = param"));
			// Variables of the program's own code are its `main`'s.
			Assert.That(Function("main"), Does.Contain("`d`").And.Contain("`p`").And.Contain("`arr`"));
		});
	}

	[Test]
	public void ClassesAreDescribedWithTheirBaseClassAndFields()
	{
		Assert.Multiple(() => {
			// Object only contains the metaclass ref; Animal is Object and then its own fields, Dog is
			// Animal and then its fields.
			Assert.That(_types, Does.Contain("LF_STRUCTURE [size = 36] `Object`").Or.Contain("`Object`"));
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `metaclass`, Type = \w+ \(void\*\), offset = 0"));
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `Legs`, Type = 0x0074 \(int\), offset = 8"));
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `Name`, Type = 0x\w+, offset = 16"));
			Assert.That(Regex.Matches(_types, "LF_BCLASS").Count, Is.GreaterThanOrEqualTo(2), "Animal and Dog each have a base class");
		});
	}

	[Test]
	public void StructsStringsAndArraysAreDescribed()
	{
		Assert.Multiple(() => {
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `X`, Type = 0x0074 \(int\), offset = 0"));
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `Y`, Type = 0x0074 \(int\), offset = 4"));
			// A string contains its length and then its bytes (what the natvis reads);
			// an array is laid out as its pointer and length.
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `len`, Type = 0x0074 \(int\), offset = 0"));
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `bytes`, Type = 0x\w+, offset = 4"));
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `data`, Type = 0x0674 \(int\*\), offset = 0"));
			Assert.That(_types, Does.Match(@"LF_MEMBER \[name = `length`, Type = 0x0074 \(int\), offset = 8"));
		});
	}

	[Test]
	public async Task ModuleFieldsAreDescribed()
		=> Assert.That(await PdbUtil("--globals"), Does.Match(@"S_GDATA32 \[size = \d+\] `Util::Count`"));

	[Test]
	public async Task LineNumbersFollowTheSource()
	{
		var lines = await PdbUtil("-l");
		var seen = Regex.Matches(lines, @"(?<line>\d+) [0-9A-F]{8} !").Select(m => int.Parse(m.Groups["line"].Value)).ToHashSet();
		int[] expected = [
			LineOf("constructor virtual Create()"), LineOf("def virtual Speak()"), LineOf("return \"...\""),
			LineOf("constructor override Create()"), LineOf("Name = \"rex\""), LineOf("return \"woof\""),
			LineOf("let total = a + b"), LineOf("return total"),
			LineOf("let d = Dog.Create()"), LineOf("print d.Speak()"), LineOf("print arr[0].ToString()"),
		];

		Assert.That(expected.Except(seen), Is.Empty, "lines of the source with no code");
	}

	[Test]
	public void VariablesAreDeclaredAtTheirOwnLines()
	{
		var ir = File.ReadAllText(_exe + ".ll");

		Assert.Multiple(() => {
			Assert.That(ir, Does.Match(@"DILocalVariable\(name: ""total"".*line: " + LineOf("let total = a + b") + @"\b"));
			Assert.That(ir, Does.Match(@"DILocalVariable\(name: ""arr"".*line: " + LineOf("var arr = new int[4]") + @"\b"));
		});
	}

	[Test]
	public void TheVisualizerIsWrittenWithTheProgram()
	{
		var natvis = File.ReadAllText(_exe + ".natvis");

		Assert.Multiple(() => {
			Assert.That(natvis, Does.Contain("<Type Name=\"string\">"));
			Assert.That(natvis, Does.Contain("<Type Name=\"Object\">"));
		});
	}

	// A source file's name, so a debugger can open it.
	[Test]
	public async Task SourceFilesAreRecorded()
	{
		var directory = Path.Combine(Folder, "dbgsource");
		Directory.CreateDirectory(directory);
		var source = Path.Combine(directory, "greeting.pwrfl");
		File.WriteAllText(source, "def Twice(n: int): int\n\treturn n * 2\nend\n\nprint Twice(21).ToString()\n");
		var exe = Path.Combine(Folder, "dbgfile.exe");
		var result = new PwrCompilerP3().Compile(new CompileOptions([CodeSource.FromFile(source)], exe, [Folder], DebugInfo: true));
		await Assert.MultipleAsync(async () => {
			Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
			Assert.That(await PdbUtil("-l", Path.ChangeExtension(exe, ".pdb")), Does.Contain("greeting.pwrfl"));
		});
	}

	[Test]
	public async Task ClassesFromAnotherLibraryBuildWithDebugInfo()
	{
		var library = new PwrCompilerP3().Compile(new CompileOptions([CodeSource.FromText("""
			def _DllMainCRTStartup(instance: ptr, reason: int, reserved: ptr): int
				return 1
			end

			class Base
				Weight: int = 5

				constructor virtual Create()
					super Create()
				end

				def virtual Speak(): string
					return "base"
				end
			end
			""")], Path.Combine(Folder, "dbglib.dll"), [Folder], ProjectType: ProjectType.Library, DebugInfo: true));
		Assert.That(library, Is.InstanceOf<BuildCompileResult>(), (library as ErrorCompileResult)?.Error);

		var exe = Path.Combine(Folder, "dbgextends.exe");
		var result = new PwrCompilerP3().Compile(new CompileOptions([CodeSource.FromText("""
			class Derived extends Base
				constructor override Create()
					super Create()
				end

				def override Speak(): string
					return "derived " + Weight.ToString()
				end
			end

			var d = Derived.Create()
			var b: Base = d
			print b.Speak()
			""")], exe, [Folder], Imports: ["dbglib.dll"], DebugInfo: true));
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);

		var process = Process.Start(new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false })!;
		await Assert.MultipleAsync(async () => {
			Assert.That(process.WaitForExit(TimeSpan.FromSeconds(5)), Is.True);
			Assert.That(process.ExitCode, Is.EqualTo(0));
			Assert.That(process.StandardOutput.ReadToEnd().Replace("\r\n", "\n"), Is.EqualTo("derived 5\n"));
			Assert.That(await PdbUtil("--types", Path.ChangeExtension(exe, ".pdb")), Does.Match(@"LF_MEMBER \[name = `Weight`, Type = 0x0074 \(int\), offset = 8"));
		});
	}
}
