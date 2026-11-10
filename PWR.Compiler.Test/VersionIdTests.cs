using System.Diagnostics;

using PWR.Compiler.Metadata;
using PWR.Compiler.TypeSystem.External;

namespace PWR.Compiler.Test;

// A library's version ID is a hash of its metadata and the version IDs of the libraries it was built
// against. It stays the same through changes to implementation details, and changes when anything
// externally visible changes, and a program, or another library, that finds a different ID than it
// was built against refuses to load.
internal class VersionIdTests
{
	private static string TempFolder => ExternalClassTests.TempFolder;

	private const int LIBRARY_MISMATCH = 0x50570001;

	private const string Startup = """
		def _DllMainCRTStartup(instance: ptr, reason: int, reserved: ptr): int
			return 1
		end


		""";

	private static string BaseSource(string answer = "1", string extra = "") => Startup + $$"""
		module VerBase
			def Answer(): int
				return {{answer}}
			end
		{{extra}}
		end
		""";

	private const string MidSource = Startup + """
		module VerMid
			def Twice(): int
				return VerBase.Answer() * 2
			end
		end
		""";

	[OneTimeSetUp]
	public void SetUp()
	{
		Directory.CreateDirectory(TempFolder);
		ExternalClassTests.EnsureRuntime();
	}

	private static string PathOf(string library) => Path.Combine(TempFolder, library + ".dll");

	private static string BuildLibrary(string name, string source, params string[] imports)
	{
		var path = PathOf(name);
		var result = new PwrCompilerP3().Compile(new CompileOptions(
			[CodeSource.FromText(source)], path, [TempFolder], Imports: imports, ProjectType: ProjectType.Library));
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
		return path;
	}

	private static ExternalLibrary Read(string path)
	{
		using var stream = File.OpenRead(path);
		return new ExternalLibrary(new MetadataContext(stream), Path.GetFileNameWithoutExtension(path));
	}

	private static Guid IdOf(string name, string source, params string[] imports)
		=> Read(BuildLibrary(name, source, imports)).Id;

	private static CompileResult BuildProgram(string name, string code, params string[] imports)
		=> new PwrCompilerP3().Compile(new CompileOptions(
			[CodeSource.FromText(code)], Path.Combine(TempFolder, name + ".exe"), [TempFolder], Imports: imports));

	private static (int ExitCode, string Output, string Error) RunProgram(string name, string code, params string[] imports)
	{
		var result = BuildProgram(name, code, imports);
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
		return Run(((BuildCompileResult)result).Filename);
	}

	private static (int ExitCode, string Output, string Error) Run(string filename)
	{
		var process = Process.Start(new ProcessStartInfo(filename) {
			RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
		})!;
		var output = process.StandardOutput.ReadToEndAsync();
		var error = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit(TimeSpan.FromSeconds(5))) {
			process.Kill();
			Assert.Fail("Timeout running the program");
		}
		return (process.ExitCode, output.Result.Replace("\r\n", "\n"), error.Result);
	}

	[Test]
	public void CheckV5Guid()
	{
		var id = IdOf("verid-shape", BaseSource());
		var bytes = id.ToByteArray(bigEndian: true);
		Assert.Multiple(() => {
			Assert.That(bytes[6] >> 4, Is.EqualTo(5), "version");
			Assert.That(bytes[8] >> 6, Is.EqualTo(2), "variant");
		});
	}

	[Test]
	public void HashGuidIsDeterministic()
	{
		var v1 = IdOf("verid-same", BaseSource());
		var v2 = IdOf("verid-same", BaseSource());
		Assert.That(v1, Is.EqualTo(v2));
	}

	[Test]
	public void ImplementationDetailsDoNotChangeHashGuid()
		=> Assert.That(IdOf("verid-impl", BaseSource(answer: "1")), Is.EqualTo(IdOf("verid-impl", BaseSource(answer: "2 + 3"))));

	[Test]
	public void NewFunctionChangesHashGuid()
		=> Assert.That(
			IdOf("verid-func", BaseSource()),
			Is.Not.EqualTo(IdOf("verid-func", BaseSource(extra: "def Other(): int\n\t\treturn 2\n\tend"))));

	[Test]
	public void NewFieldChangesHashGuid()
	{
		const string Plain = Startup + """
			class Thing
				constructor Create()
					super Create()
				end
			end
			""";
		const string WithField = Startup + """
			class Thing
				Size: int = 1

				constructor Create()
					super Create()
				end
			end
			""";
		Assert.That(IdOf("verid-field", Plain), Is.Not.EqualTo(IdOf("verid-field", WithField)));
	}

	[Test]
	public void NewConstantValueChangesHashGuid()
	{
		static string Source(int value) => Startup + $$"""
			class Thing
				constructor Create()
					super Create()
				end

				const class Sides: int = {{value}}
			end
			""";
		Assert.That(IdOf("verid-const", Source(3)), Is.Not.EqualTo(IdOf("verid-const", Source(4))));
	}

	[Test]
	public void DependenciesAreRecorded()
	{
		var baseId = Read(BuildLibrary("verbase", BaseSource())).Id;
		var mid = Read(BuildLibrary("vermid", MidSource, "verbase.dll"));
		var runtime = Read(PathOf("pwr"));

		Assert.That(mid.Dependencies, Is.EquivalentTo([("pwr", runtime.Id), ("verbase", baseId)]));
	}

	[Test]
	public void NewDependencyChangesHashGuid()
	{
		BuildLibrary("verbase", BaseSource());
		var before = IdOf("vermid", MidSource, "verbase.dll");
		BuildLibrary("verbase", BaseSource(extra: "def Other(): int\n\t\treturn 2\n\tend"));
		var after = IdOf("vermid", MidSource, "verbase.dll");

		Assert.That(after, Is.Not.EqualTo(before));
	}

	[Test]
	public void ImplementationDetailsDoNotChangeDownstreamHashGuids()
	{
		BuildLibrary("verbase", BaseSource(answer: "1"));
		var before = IdOf("vermid", MidSource, "verbase.dll");
		BuildLibrary("verbase", BaseSource(answer: "7"));
		var after = IdOf("vermid", MidSource, "verbase.dll");

		Assert.That(after, Is.EqualTo(before));
	}

	[Test]
	public void CompatibleHashGuidsRunSuccessfully()
	{
		BuildLibrary("verbase", BaseSource());
		BuildLibrary("vermid", MidSource, "verbase.dll");
		var (exitCode, output, _) = RunProgram("verprog-ok", "print VerMid.Twice().ToString()", "vermid.dll", "verbase.dll");

		Assert.Multiple(() => {
			Assert.That(exitCode, Is.EqualTo(0));
			Assert.That(output, Is.EqualTo("2\n"));
		});
	}

	private const string AnnouncingBaseSource = Startup + """
		module VerBase
			Started: int = Announce()

			def Announce(): int
				print "base init"
				return 1
			end

			def Answer(): int
				return 1
			end
		end
		""";

	[Test]
	public void TransitiveImportsInitializeSuccessfully()
	{
		BuildLibrary("verbase", AnnouncingBaseSource);
		BuildLibrary("vermid", MidSource, "verbase.dll");
		var (exitCode, output, _) = RunProgram("verprog-chain", "print VerMid.Twice().ToString()", "vermid.dll");

		Assert.Multiple(() => {
			Assert.That(exitCode, Is.EqualTo(0));
			Assert.That(output, Is.EqualTo("base init\n2\n"));
		});
	}

	[Test]
	public void ModulesInitializeInOrder()
	{
		BuildLibrary("verbase", AnnouncingBaseSource);
		var (exitCode, output, _) = RunProgram("verprog-order", """
			module Mine
				Started: int = Announce()

				def Announce(): int
					print "program init"
					return VerBase.Answer()
				end
			end

			print "main"
			print Mine.Started.ToString()
			""", "verbase.dll");

		Assert.Multiple(() => {
			Assert.That(exitCode, Is.EqualTo(0));
			Assert.That(output, Is.EqualTo("base init\nprogram init\nmain\n1\n"));
		});
	}

	[Test]
	public void IdempotentInitialization()
	{
		BuildLibrary("verbase", AnnouncingBaseSource);
		BuildLibrary("vermid", MidSource, "verbase.dll");
		var (exitCode, output, _) = RunProgram("verprog-diamond", "print VerMid.Twice().ToString()", "verbase.dll", "vermid.dll");

		Assert.Multiple(() => {
			Assert.That(exitCode, Is.EqualTo(0));
			Assert.That(output, Is.EqualTo("base init\n2\n"));
		});
	}

	[Test]
	public void ImplementationChangesDoNotBlockExecution()
	{
		BuildLibrary("verbase", BaseSource(answer: "1"));
		BuildLibrary("vermid", MidSource, "verbase.dll");
		var program = BuildProgram("verprog-impl", "print VerMid.Twice().ToString()", "vermid.dll", "verbase.dll");
		Assert.That(program, Is.InstanceOf<BuildCompileResult>(), (program as ErrorCompileResult)?.Error);
		BuildLibrary("verbase", BaseSource(answer: "10"));

		var (exitCode, output, _) = Run(((BuildCompileResult)program).Filename);

		Assert.Multiple(() => {
			Assert.That(exitCode, Is.EqualTo(0));
			Assert.That(output, Is.EqualTo("20\n"));
		});
	}

	[Test]
	public void CompilerCatchesStaleHashGuids()
	{
		BuildLibrary("verbase", BaseSource());
		BuildLibrary("vermid", MidSource, "verbase.dll");
		BuildLibrary("verbase", BaseSource(extra: "def Other(): int\n\t\treturn 2\n\tend"));

		var result = BuildProgram("verprog-disagree", "print VerBase.Answer().ToString()", "vermid.dll", "verbase.dll");

		Assert.Multiple(() => {
			Assert.That(result, Is.InstanceOf<ErrorCompileResult>());
			Assert.That(((ErrorCompileResult)result).Error, Does.Contain("Rebuild 'vermid'"));
		});
	}

	[Test]
	public void RuntimeCatchesStaleHashGuids()
	{
		BuildLibrary("verbase", BaseSource());
		var program = BuildProgram("verprog-swapped", "print VerBase.Answer().ToString()", "verbase.dll");
		Assert.That(program, Is.InstanceOf<BuildCompileResult>(), (program as ErrorCompileResult)?.Error);
		var filename = ((BuildCompileResult)program).Filename;
		Assert.That(Run(filename).Output, Is.EqualTo("1\n"));

		BuildLibrary("verbase", BaseSource(extra: "def Other(): int\n\t\treturn 2\n\tend"));
		var (exitCode, output, error) = Run(filename);

		Assert.Multiple(() => {
			Assert.That(exitCode, Is.EqualTo(LIBRARY_MISMATCH));
			Assert.That(output, Is.Empty);
			Assert.That(error, Does.Contain("'verprog-swapped' was built against a different version of 'verbase'"));
		});
	}

	[Test]
	public void RuntimeCatchesStaleHashGuids2()
	{
		BuildLibrary("verbase", BaseSource());
		BuildLibrary("vermid", MidSource, "verbase.dll");
		var program = BuildProgram("verprog-middle", "print VerMid.Twice().ToString()", "vermid.dll");
		Assert.That(program, Is.InstanceOf<BuildCompileResult>(), (program as ErrorCompileResult)?.Error);
		var filename = ((BuildCompileResult)program).Filename;
		Assert.That(Run(filename).Output, Is.EqualTo("2\n"));

		BuildLibrary("verbase", BaseSource(extra: "def Other(): int\n\t\treturn 2\n\tend"));
		var (exitCode, output, error) = Run(filename);

		Assert.Multiple(() => {
			Assert.That(exitCode, Is.EqualTo(LIBRARY_MISMATCH));
			Assert.That(output, Is.Empty);
			Assert.That(error, Does.Contain("'vermid' was built against a different version of 'verbase'"));
		});
	}
}
