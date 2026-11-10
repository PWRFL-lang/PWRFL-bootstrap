using System.Diagnostics;

namespace PWR.Compiler.Test;

// Every type belongs to a namespace, which by default is the name of the library it's in.  The
// exported symbols of everything in a namespace include its name, so two libraries can each have a
// `Tools` and a `Widget`, and a program that imports both can distinguish them by their
// fully-qualified names.
internal class NamespaceTests
{
	private static string Folder => ExternalClassTests.TempFolder;

	private const string Startup = """
		def _DllMainCRTStartup(instance: ptr, reason: int, reserved: ptr): int
			return 1
		end


		""";

	private static string Library(int n) => Startup + $$"""
		module Tools
			def Size(): int
				return {{n}}
			end
		end

		class Widget
			constructor Create()
				super Create()
			end

			def Weight(): int
				return {{n}}
			end

			def class Origin(): string
				return "library {{n}}"
			end
		end
		""";

	[OneTimeSetUp]
	public void SetUp()
	{
		Directory.CreateDirectory(Folder);
		ExternalClassTests.EnsureRuntime();
		BuildLibrary("nsone", Library(1));
		BuildLibrary("nstwo", Library(2));
	}

	private static void BuildLibrary(string name, string source)
	{
		var result = new PwrCompilerP3().Compile(new CompileOptions(
			[CodeSource.FromText(source)], Path.Combine(Folder, name + ".dll"), [Folder], ProjectType: ProjectType.Library));
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
	}

	private static CompileResult Build(string name, string code, params string[] imports)
		=> new PwrCompilerP3().Compile(new CompileOptions(
			[CodeSource.FromText(code)], Path.Combine(Folder, name + ".exe"), [Folder], Imports: imports));

	private static string Run(string name, string code, params string[] imports)
	{
		var result = Build(name, code, imports);
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
		var process = Process.Start(new ProcessStartInfo(((BuildCompileResult)result).Filename) { RedirectStandardOutput = true, UseShellExecute = false })!;
		if (!process.WaitForExit(TimeSpan.FromSeconds(5))) {
			process.Kill();
			Assert.Fail("Timeout running the program");
		}
		Assert.That(process.ExitCode, Is.EqualTo(0));
		return process.StandardOutput.ReadToEnd().Replace("\r\n", "\n");
	}

	[Test]
	public void UnqualifiedNamesAreAmbiguous()
	{
		var result = Build("nsprog-ambiguous", "print Tools.Size().ToString()", "nsone.dll", "nstwo.dll");

		Assert.Multiple(() => {
			Assert.That(result, Is.InstanceOf<ErrorCompileResult>());
			Assert.That(((ErrorCompileResult)result).Error, Does.Contain("Multiple elements named 'Tools'"));
		});
	}

	[Test]
	public void QualifiedModuleNamesDisambiguateReferences()
	{
		var output = Run("nsprog-modules", """
			print nsone.Tools.Size().ToString()
			print nstwo.Tools.Size().ToString()
			""", "nsone.dll", "nstwo.dll");

		Assert.That(output, Is.EqualTo("1\n2\n"));
	}

	[Test]
	public void QualifiedClassNamesDisambiguateReferences()
	{
		var output = Run("nsprog-classes", """
			var a = nsone.Widget.Create()
			var b = nstwo.Widget.Create()
			print a.Weight().ToString()
			print b.Weight().ToString()
			print nsone.Widget.Origin()
			print nstwo.Widget.Origin()
			""", "nsone.dll", "nstwo.dll");

		Assert.That(output, Is.EqualTo("1\n2\nlibrary 1\nlibrary 2\n"));
	}

	[Test]
	public void QualifiedNamesCanBeUsedAsTypes()
	{
		var output = Run("nsprog-types", """
			def Weigh(w: nsone.Widget): int
				return w.Weight()
			end

			def Make(): nstwo.Widget
				return nstwo.Widget.Create()
			end

			var a: nsone.Widget = nsone.Widget.Create()
			var b: nstwo.Widget = Make()
			print Weigh(a).ToString()
			print b.Weight().ToString()
			""", "nsone.dll", "nstwo.dll");

		Assert.That(output, Is.EqualTo("1\n2\n"));
	}

	[Test]
	public void AClassCanExtendAQualifiedClass()
	{
		var output = Run("nsprog-extends", """
			class Heavy extends nstwo.Widget
				constructor Create()
					super Create()
				end

				def Double(): int
					return Weight() * 2
				end
			end

			var h = Heavy.Create()
			print h.Double().ToString()
			print h.Weight().ToString()
			""", "nsone.dll", "nstwo.dll");

		Assert.That(output, Is.EqualTo("4\n2\n"));
	}

	[Test]
	public void QualifiedTypeError()
	{
		var result = Build("nsprog-notype", "var w: nsone.Nonesuch = nil", "nsone.dll");

		Assert.Multiple(() => {
			Assert.That(result, Is.InstanceOf<ErrorCompileResult>());
			Assert.That(((ErrorCompileResult)result).Error, Does.Contain("nsone.Nonesuch"));
		});
	}

	[Test]
	public void QualifiedTypeError2()
	{
		var result = Build("nsprog-missing", "print nsone.Nonesuch.Size().ToString()", "nsone.dll");

		Assert.Multiple(() => {
			Assert.That(result, Is.InstanceOf<ErrorCompileResult>());
			Assert.That(((ErrorCompileResult)result).Error, Does.Contain("does not contain a member named 'Nonesuch'"));
		});
	}

	[Test]
	public void CheckDefaultNamespace()
	{
		var output = Run("nsprogown", """
			module Tools
				def Size(): int
					return 99
				end
			end

			print nsprogown.Tools.Size().ToString()
			""", "nsone.dll");

		Assert.That(output, Is.EqualTo("99\n"));
	}

	[Test]
	public void ANameInScopeComesBeforeANamespace()
	{
		var output = Run("nsprog-shadow", """
			module nsone
				def Size(): int
					return 42
				end
			end

			print nsone.Size().ToString()
			""", "nsone.dll");

		Assert.That(output, Is.EqualTo("42\n"));
	}

	private static string[] ExportsOf(string dll)
	{
		using var stream = File.OpenRead(Path.Combine(Folder, dll));
		using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
		var headers = reader.PEHeaders;
		var directory = headers.PEHeader!.ExportTableDirectory;
		var bytes = new byte[stream.Length];
		stream.Position = 0;
		stream.ReadExactly(bytes);
		int Offset(int rva)
		{
			foreach (var section in headers.SectionHeaders) {
				if (rva >= section.VirtualAddress && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData)) {
					return rva - section.VirtualAddress + section.PointerToRawData;
				}
			}
			throw new InvalidOperationException();
		}
		var table = Offset(directory.RelativeVirtualAddress);
		var count = BitConverter.ToInt32(bytes, table + 24);
		var names = Offset(BitConverter.ToInt32(bytes, table + 32));
		var result = new List<string>();
		for (var i = 0; i < count; ++i) {
			var start = Offset(BitConverter.ToInt32(bytes, names + i * 4));
			var end = Array.IndexOf(bytes, (byte)0, start);
			result.Add(System.Text.Encoding.ASCII.GetString(bytes, start, end - start));
		}
		return [.. result];
	}

	[Test]
	public void SymbolsOfTheSameNamedMemberDifferByNamespace()
	{
		var one = ExportsOf("nsone.dll");
		var two = ExportsOf("nstwo.dll");

		Assert.Multiple(() => {
			Assert.That(one, Does.Contain("nsone$$Tools$Size"));
			Assert.That(two, Does.Contain("nstwo$$Tools$Size"));
			Assert.That(one.Intersect(two).Where(s => s.Contains("Tools") || s.Contains("Widget")), Is.Empty);
		});
	}
}
