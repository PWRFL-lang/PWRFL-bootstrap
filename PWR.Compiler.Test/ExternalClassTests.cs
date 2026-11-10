using System.Diagnostics;
using System.Runtime.InteropServices;

using PWR.Compiler.Ast;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.External;
using PWR.Compiler.TypeSystem.Internal;

namespace PWR.Compiler.Test;

// Classes in another library: reading them back from its metadata, and using them from a program
// that imports it.
internal class ExternalClassTests
{
	private const string LibrarySource = """
		def _DllMainCRTStartup(instance: ptr, reason: int, reserved: ptr): int
			return 1
		end

		class Square extends Shape
			Side: int = 3

			constructor override Create()
				super Create()
			end

			const class override Sides = 4
			const class override Id = 7

			def override Area(): int
				return Side * Side
			end

			def class override Kind(): string
				return "square"
			end
		end

		class Circle extends Shape
			constructor override Create()
				super Create()
			end

			const class override Id = 8

			def override Area(): int
				return 3
			end
		end

		class abstract Shape
			constructor virtual Create()
				super Create()
			end

			const class Sides: int = 0
			const class Default: meta Shape = meta Circle
			const class abstract Id: int

			def abstract Area(): int

			def class Kind(): string
				return "shape"
			end

			def Describe(): string
				return "a shape"
			end
		end

		module Factory
			def MakeSquare(): Shape
				return Square.Create()
			end

			def SquareClass(): meta Shape
				return meta Square
			end
		end
		""";

	internal static string TempFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pwr");

	private ExternalLibrary _library = null!;

	[OneTimeSetUp]
	public void BuildLibrary()
	{
		Directory.CreateDirectory(TempFolder);
		EnsureRuntime();
		var path = Path.Combine(TempFolder, "shapes.dll");
		var result = new PwrCompilerP3().Compile(new CompileOptions(
			[CodeSource.FromText(LibrarySource)], path, [TempFolder], ProjectType: ProjectType.Library));
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
		using var stream = File.OpenRead(path);
		_library = new ExternalLibrary(new MetadataContext(stream), "shapes");
	}

	// The runtime library every program links against, built the way the bootstrap tests build it.
	internal static void EnsureRuntime()
	{
		var runtimeSource = Directory.EnumerateFiles(".\\Code", "pwr*.pwrfl").OrderByDescending(f => f).First();
		var memorySource = Directory.EnumerateFiles(".\\Code", "mem*.pwrfl").OrderByDescending(f => f).First();
		string[] inputs = [runtimeSource, memorySource, typeof(PwrCompilerP3).Assembly.Location];
		var runtime = Path.Combine(TempFolder, "pwr.dll");
		if (inputs.Max(File.GetLastWriteTime) > File.GetLastWriteTime(runtime)) {
			var result = new PwrCompilerP3().Compile(new CompileOptions(
				[CodeSource.FromFile(runtimeSource), CodeSource.FromFile(memorySource)], runtime, [],
				ProjectType: ProjectType.Library, NoStdLib: true));
			Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
		}
	}

	private IClassType Class(string name) => (ExternalClass)_library.Types.Single(t => t.Name == name);

	[Test]
	public void ReadClasses()
	{
		Assert.Multiple(() => {
			Assert.That(Class("Shape"), Is.InstanceOf<IClassType>());
			Assert.That(Class("Square"), Is.InstanceOf<IClassType>());
			Assert.That(Class("Circle"), Is.InstanceOf<IClassType>());
			Assert.That(_library.Types.Single(t => t.Name == "Factory"), Is.InstanceOf<ExternalModule>());
		});
	}

	[Test]
	public void ReadBaseTypes()
	{
		Assert.Multiple(() => {
			Assert.That(Class("Square").BaseType, Is.SameAs(Class("Shape")));
			Assert.That(Class("Circle").BaseType, Is.SameAs(Class("Shape")));
			Assert.That(Class("Shape").BaseType, Is.SameAs(ObjectType.Instance));
		});
	}

	[Test]
	public void ReadAbstractClasses()
	{
		Assert.Multiple(() => {
			Assert.That(Class("Shape").IsAbstract, Is.True);
			Assert.That(Class("Square").IsAbstract, Is.False);
		});
	}

	[Test]
	public void ReadDeclaredInstanceFields()
	{
		var side = Class("Square").Fields.Single();
		Assert.Multiple(() => {
			Assert.That(side.Name, Is.EqualTo("Side"));
			Assert.That(side.Type.Name, Is.EqualTo("int"));
			Assert.That(Class("Shape").Fields, Is.Empty);
			Assert.That(Class("Circle").Fields, Is.Empty);
		});
	}

	[Test]
	public void ReadMethods()
	{
		Assert.Multiple(() => {
			Assert.That(Class("Shape").Methods.Select(m => ((ISemantic)m).Name), Is.EquivalentTo([ "Create", "Area", "Kind", "Describe" ]));
			Assert.That(Class("Square").Methods.Select(m => ((ISemantic)m).Name), Is.EquivalentTo([ "Create", "Area", "Kind" ]));
		});
	}

	// The metadata doesn't store the VMT. It's derived from the base class's table and the
	// members'slots.  It has to come out identical to the one the compiler built.
	[Test]
	public void RebuildVmtFromMetadata()
	{
		var shape = Class("Shape").Vmt;
		var square = Class("Square").Vmt;
		var circle = Class("Circle").Vmt;

		Assert.Multiple(() => {
			Assert.That(shape.Select(e => e.Name), Is.EqualTo([ "Create", "Sides", "Default", "Id", "Area", "Kind" ]));
			Assert.That(square.Select(e => e.Name), Is.EqualTo(shape.Select(e => e.Name)));
			Assert.That(Class("Shape").VmtSlotCount, Is.EqualTo(6));
			Assert.That(Class("Square").SlotOf("Area"), Is.EqualTo(4));
			Assert.That(Class("Shape").SlotOf("Describe"), Is.EqualTo(-1));

			// check slot kinds
			Assert.That(shape.Select(e => e.IsConstant), Is.EqualTo([ false, true, true, true, false, false ]));
			Assert.That(shape.Select(e => e.IsAbstract), Is.EqualTo([ false, false, false, true, true, false ]));
			Assert.That(shape[4].IsClassScope, Is.False);
			Assert.That(shape[5].IsClassScope, Is.True);
			Assert.That(square.Any(e => e.IsAbstract), Is.False);

			// an override replaces the entry, and what isn't overridden is the base class's
			Assert.That(shape[4].FullName, Is.EqualTo("shapes$$Shape$Area"));
			Assert.That(square[0].FullName, Is.EqualTo("shapes$$Square$Create"));
			Assert.That(square[4].FullName, Is.EqualTo("shapes$$Square$Area"));
			Assert.That(circle[4].FullName, Is.EqualTo("shapes$$Circle$Area"));
			Assert.That(circle[5].FullName, Is.EqualTo("shapes$$Shape$Kind"));
		});
	}

	[Test]
	public void ReadConstants()
	{
		var shape = Class("Shape").Vmt;
		var square = Class("Square").Vmt;
		var circle = Class("Circle").Vmt;

		Assert.Multiple(() => {
			Assert.That(shape[1].ConstantType!.Name, Is.EqualTo("int"));
			Assert.That(shape[1].Value, Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(0));
			Assert.That(square[1].Value, Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(4));
			Assert.That(circle[1].Value, Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(0));
			// an abstract constant has no value
			Assert.That(shape[3].Value, Is.Null);
			Assert.That(square[3].Value, Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(7));
			Assert.That(circle[3].Value, Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(8));
		});
	}

	[Test]
	public void MetaclassConstantIsReadAsTheClass()
	{
		var entry = Class("Shape").Vmt[2];
		Assert.Multiple(() => {
			Assert.That(entry.ConstantType, Is.InstanceOf<MetaclassType>().With.Property("Class").SameAs(Class("Shape")));
			Assert.That(entry.Value, Is.InstanceOf<MetaclassExpression>());
			Assert.That(((MetaclassExpression)entry.Value!).Type.Semantic!.Type, Is.SameAs(Class("Circle")));
		});
	}

	[Test]
	public void MembersAreFound()
	{
		var square = (IType)Class("Square");
		var circle = (IType)Class("Circle");
		var create = (ExternalMethod)square.GetMember("Create")!;

		Assert.Multiple(() => {
			Assert.That(square.GetMember("Area"), Is.InstanceOf<ExternalMethod>());
			Assert.That(square.GetMember("Sides"), Is.InstanceOf<IClassConstant>().With.Property("Name").EqualTo("Sides"));
			Assert.That(square.GetMember("Nonesuch"), Is.Null);

			Assert.That(create.SemanticType.HasFlag(SemanticType.Constructor), Is.True);
			Assert.That(create.SemanticType.HasFlag(SemanticType.HasSelf), Is.False);
			Assert.That(create.Type, Is.SameAs(square));
			Assert.That(create.Slot, Is.EqualTo(0));
			Assert.That(create.Flags.HasFlag(FunctionFlags.Override), Is.True);

			Assert.That(circle.GetMember("Kind"), Is.InstanceOf<ExternalMethod>().With.Property("FullName").EqualTo("shapes$$Shape$Kind"));
			Assert.That(circle.GetMember("Sides"), Is.InstanceOf<IClassConstant>());
			Assert.That(circle.GetMember("Describe"), Is.InstanceOf<ExternalMethod>().With.Property("FullName").EqualTo("shapes$$Shape$Describe"));
			Assert.That(square.GetMember("Side"), Is.InstanceOf<ExternalField>());
		});
	}

	[Test]
	public void ExternalMetaclassTypeSeesClassScopeMembers()
	{
		var meta = MetaclassType.Create(Class("Square"));
		Assert.Multiple(() => {
			Assert.That(meta.GetMember("Create"), Is.Not.Null);
			Assert.That(meta.GetMember("Kind"), Is.Not.Null);
			Assert.That(meta.GetMember("Sides"), Is.Not.Null);
			Assert.That(meta.GetMember("Area"), Is.Null);
			Assert.That(meta.CanAssignTo(MetaclassType.Create(Class("Shape"))), Is.True);
			Assert.That(MetaclassType.Create(Class("Shape")).CanAssignTo(meta), Is.False);
		});
	}

	[Test]
	public void CheckAssignabilityRules()
	{
		Assert.Multiple(() => {
			Assert.That(Class("Square").CanAssignTo(Class("Shape")), Is.True);
			Assert.That(Class("Square").CanAssignTo(Class("Square")), Is.True);
			Assert.That(Class("Square").CanAssignTo(ObjectType.Instance), Is.True);
			Assert.That(Class("Shape").CanAssignTo(Class("Square")), Is.False);
			Assert.That(Class("Square").CanAssignTo(Class("Circle")), Is.False);
		});
	}

	// Builds the code into a program that imports the given libraries (the shapes library, if none), and runs it.
	private static string Run(string code, params string[] imports)
	{
		var options = new CompileOptions(
			[CodeSource.FromText(code)], Path.Combine(TempFolder, "useshapes.exe"), [TempFolder],
			Imports: imports.Length > 0 ? imports : ["shapes.dll"]);
		var result = new PwrCompilerP3().Compile(options);
		if (result is not BuildCompileResult { Filename: { } filename }) {
			Assert.Fail("Build failed: " + ((ErrorCompileResult)result).Error);
			throw new UnreachableException();
		}
		var process = Process.Start(new ProcessStartInfo(filename) { RedirectStandardOutput = true, UseShellExecute = false })!;
		if (!process.WaitForExit(TimeSpan.FromSeconds(5))) {
			process.Kill();
			Assert.Fail("Timeout running the program");
		}
		Assert.That(process.ExitCode, Is.EqualTo(0));
		return process.StandardOutput.ReadToEnd().Replace("\r\n", "\n");
	}

	private static void AssertCompileError(string code, string expected)
	{
		var options = new CompileOptions(
			[CodeSource.FromText(code)], Path.Combine(TempFolder, "useshapes.exe"), [TempFolder], Imports: ["shapes.dll"]);
		var result = new PwrCompilerP3().Compile(options);
		Assert.Multiple(() => {
			Assert.That(result, Is.InstanceOf<ErrorCompileResult>(), "Expected a compile error, but the build succeeded.");
			Assert.That(((ErrorCompileResult)result).Error, Does.Contain(expected));
		});
	}

	[Test]
	public void ExternalClassesCanBeUsedFromAProgram()
	{
		var output = Run("""
			var s = Factory.MakeSquare()
			print s.Area().ToString()
			print s.Sides.ToString()
			print s.Id.ToString()
			print s.Kind()
			print Shape.Sides.ToString()
			var m = Factory.SquareClass()
			print m.Sides.ToString()
			print m.Kind()
			var t = m.Create()
			print t.Area().ToString()
			""");

		Assert.That(output, Is.EqualTo("9\n4\n7\nsquare\n0\n4\nsquare\n9\n"));
	}

	[Test]
	public void ConstructExternalClasses()
	{
		var output = Run("""
			var s = Square.Create()
			print s.Area().ToString()
			print s.Id.ToString()
			""");

		Assert.That(output, Is.EqualTo("9\n7\n"));
	}

	[Test]
	public void ExternalClassMetaclassExpression()
	{
		var output = Run("""
			var m: meta Shape = meta Square
			print m.Kind()
			print m.Sides.ToString()
			var c: meta Shape = meta Circle
			print c.Id.ToString()
			var t = m.Create()
			print t.Area().ToString()
			""");

		Assert.That(output, Is.EqualTo("square\n4\n8\n9\n"));
	}

	[Test]
	public void CallExternalClassScopeMethod()
	{
		var output = Run("""
			print Square.Kind()
			print Circle.Kind()
			print Shape.Kind()
			""");

		Assert.That(output, Is.EqualTo("square\nshape\nshape\n"));
	}

	[Test]
	public void ExternalConstantHoldingAMetaclassCanBeUsed()
	{
		var output = Run("""
			var c = Shape.Default.Create()
			print c.Area().ToString()
			print c.Id.ToString()
			""");

		Assert.That(output, Is.EqualTo("3\n8\n"));
	}

	[Test]
	public void ExternalMetaclassIsTheLibrarysOwn()
	{
		var output = Run("""
			var inLibrary = Factory.SquareClass()
			var here: meta Shape = meta Square
			print if inLibrary == here then "same" else "different"
			var other: meta Shape = meta Circle
			print if inLibrary == other then "same" else "different"
			""");

		Assert.That(output, Is.EqualTo("same\ndifferent\n"));
	}

	[Test]
	public void ClassCanExtendAClassFromAnotherLibrary()
	{
		var output = Run("""
			class Cube extends Square
				constructor override Create()
					super Create()
				end

				const class override Sides = 12
				const class override Id = 9

				def override Area(): int
					return Side * Side * 6
				end
			end

			let c = Cube.Create()
			print c.Area().ToString()
			print c.Sides.ToString()
			print c.Id.ToString()
			let s: Shape = c
			print s.Area().ToString()
			print s.Id.ToString()
			print s.Describe()
			""");

		Assert.That(output, Is.EqualTo("54\n12\n9\n54\n9\na shape\n"));
	}

	[Test]
	public void CheckInitializationOrder()
	{
		var output = Run("""
			class Cube extends Square
				constructor override Create()
					super Create()
				end
			end

			module Setup
				Made: string = Describe()

				def Describe(): string
					return Cube.Kind()
				end
			end

			print Setup.Made
			""");

		Assert.That(output, Is.EqualTo("square\n"));
	}

	[Test]
	public void ImplicitConstructorOverride()
	{
		var output = Run("""
			class Triangle extends Shape
				Side: int = 5

				def override Area(): int
					return Side * Side // 2
					; Should be multiplied by sqrt(3)/4, or approximately 0.433, but that's not supported yet
				end

				const class override Id = 3
			end

			const m: meta Shape = meta Triangle
			let t = m.Create()
			print t.Area().ToString()
			""");

		Assert.That(output, Is.EqualTo("12\n"));
	}

	private static void BuildImplicitConstructorLibrary()
	{
		var library = new PwrCompilerP3().Compile(new CompileOptions([CodeSource.FromText("""
			def _DllMainCRTStartup(instance: ptr, reason: int, reserved: ptr): int
				return 1
			end

			class Base
				constructor virtual Create()
					super Create()
				end

				def virtual Value(): int
					return 1
				end
			end

			class Derived extends Base
				Extra: int = 7

				def override Value(): int
					return Extra
				end
			end

			class Plain extends Base
				def override Value(): int
					return 2
				end
			end
			""")], Path.Combine(TempFolder, "implib.dll"), [TempFolder], ProjectType: ProjectType.Library));
		Assert.That(library, Is.InstanceOf<BuildCompileResult>(), (library as ErrorCompileResult)?.Error);
	}

	[Test]
	public void ImplicitConstructorOverride2()
	{
		BuildImplicitConstructorLibrary();

		var output = Run("""
			const any: meta Base = meta Derived
			let made = any.Create()
			print made.Value().ToString()
			const plain: meta Base = meta Plain
			print plain.Create().Value().ToString()
			""", "implib.dll");

		Assert.That(output, Is.EqualTo("7\n2\n"));
	}

	[Test]
	public void ImplicitConstructorOverrideOnAddedFieldsOnly()
	{
		BuildImplicitConstructorLibrary();
		using var stream = File.OpenRead(Path.Combine(TempFolder, "implib.dll"));
		var library = new ExternalLibrary(new MetadataContext(stream), "implib");
		string CreateOf(string type) => ((IClassType)library.Types.Single(t => t.Name == type)).Vmt.Single(e => e.Name == "Create").FullName;

		Assert.Multiple(() => {
			Assert.That(CreateOf("Base"), Is.EqualTo("implib$$Base$Create"));
			Assert.That(CreateOf("Derived"), Is.EqualTo("implib$$Derived$Create"));
			Assert.That(CreateOf("Plain"), Is.EqualTo("implib$$Base$Create"));
		});
	}

	[Test]
	public void FindInheritedMembers()
	{
		var output = Run("""
			class Cube extends Square
				constructor override Create()
					super Create()
				end
			end

			var c = Cube.Create()
			print c.Describe()
			print c.Side.ToString()
			c.Side = 4
			print c.Area().ToString()
			print c.Kind()
			print c.Sides.ToString()
			""");

		Assert.That(output, Is.EqualTo("a shape\n3\n16\nsquare\n4\n"));
	}

	[Test]
	public void FindInheritedMembers2()
	{
		BuildCubeLibrary();
		var output = Run("""
			var c = Cube.Create()
			print c.Describe()
			print c.Side.ToString()
			c.Side = 5
			print c.Area().ToString()
			print c.Id.ToString()
			""", "shapes.dll", "cubelib.dll");

		Assert.That(output, Is.EqualTo("a shape\n3\n10\n7\n"));
	}

	[Test]
	public void OverrideExternalVirtuals()
	{
		var output = Run("""
			class Cube extends Square
				constructor override Create()
					super Create()
				end
			end

			class Tesseract extends Cube
				constructor override Create()
					super Create()
				end

				const class override Sides = 36
			end

			var c = Cube.Create()
			var s: Shape = c
			print s.Area().ToString()
			print s.Id.ToString()
			print Tesseract.Kind()
			print Tesseract.Sides.ToString()
			""");

		Assert.That(output, Is.EqualTo("9\n7\nsquare\n36\n"));
	}

	[Test]
	public void CheckExternalFields()
	{
		var output = Run("""
			var s = Square.Create()
			print s.Side.ToString()
			s.Side = 5
			print s.Area().ToString()
			""");

		Assert.That(output, Is.EqualTo("3\n25\n"));
	}

	[Test]
	public void CheckExternalMetaclassConstants()
	{
		var output = Run("""
			class Holder
				constructor Create()
					super Create()
				end

				const class Which: meta Shape = meta Square
			end

			let h = Holder.Create()
			print h.Which.Kind()
			print h.Which.Sides.ToString()
			let made = h.Which.Create()
			print made.Area().ToString()
			""");

		Assert.That(output, Is.EqualTo("square\n4\n9\n"));
	}

	private const string CubeLibrarySource = """
		def _DllMainCRTStartup(instance: ptr, reason: int, reserved: ptr): int
			return 1
		end

		class Cube extends Square
			constructor override Create()
				super Create()
			end

			const class override Sides = 3

			def override Area(): int
				return Side * 2
			end
		end
		""";

	private static void BuildCubeLibrary()
	{
		var result = new PwrCompilerP3().Compile(new CompileOptions(
			[CodeSource.FromText(CubeLibrarySource)], Path.Combine(TempFolder, "cubelib.dll"), [TempFolder],
			Imports: ["shapes.dll"], ProjectType: ProjectType.Library));
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
	}

	[Test]
	public void ExternalClassInheritance()
	{
		BuildCubeLibrary();
		var output = Run("""
			var c = Cube.Create()
			print c.Area().ToString()
			print c.Sides.ToString()
			print Cube.Kind()
			var s: Shape = c
			print s.Area().ToString()
			print s.Id.ToString()
			""", "shapes.dll", "cubelib.dll");

		Assert.That(output, Is.EqualTo("6\n3\nsquare\n6\n7\n"));
	}

	[Test]
	public void ExternalClassInheritance2()
	{
		BuildCubeLibrary();
		var output = Run("""
			class Cube2 extends Cube
				constructor override Create()
					super Create()
				end
			end

			var c = Cube2.Create()
			var s: Cube = c
			print s.Area().ToString()
			print Cube2.Kind()
			print Cube2.Sides.ToString()
			print s.Id.ToString()
			print Cube2.Default.Kind()
			""", "cubelib.dll");

		Assert.That(output, Is.EqualTo("6\nsquare\n3\n7\nshape\n"));
	}

	const int HEADER_SIZE = 32; // the parent, the size, the name's address and its length		
	static IntPtr SlotOf(IntPtr metaclass, int slot) => Marshal.ReadIntPtr(metaclass, HEADER_SIZE + slot * IntPtr.Size);

	[Test]
	public void CheckMetaclassInitialization()
	{
		BuildCubeLibrary();

		var directory = Path.Combine(TempFolder, "in-process");
		Directory.CreateDirectory(directory);
		foreach (var name in new[] { "pwr.dll", "shapes.dll", "cubelib.dll" }) {
			File.Copy(Path.Combine(TempFolder, name), Path.Combine(directory, name), overwrite: true);
		}
		var cubelibrary = NativeLibrary.Load(Path.Combine(directory, "cubelib.dll"));
		var shapesLibrary = NativeLibrary.Load(Path.Combine(directory, "shapes.dll"));
		var cube = NativeLibrary.GetExport(cubelibrary, "cubelib$$Cube$Metaclass");
		var square = NativeLibrary.GetExport(shapesLibrary, "shapes$$Square$Metaclass");

		// What the library says it has: which slots are its base class's, and which are its own.
		using var stream = File.OpenRead(Path.Combine(TempFolder, "cubelib.dll"));
		var context = new MetadataContext(stream) {
			ImportResolver = (library, ns, type) => library == "shapes"
				? _library.Types.Single(t => t.Name == type)
				: throw new InvalidOperationException($"Unexpected library '{library}'"),
		};
		var cubeCls = (IClassType)new ExternalLibrary(context, "cubelib").Types.Single(t => t.Name == "Cube");
		var baseVmt = Class("Square").Vmt;
		var inherited = Enumerable.Range(0, baseVmt.Length).Where(i => cubeCls.Vmt[i].FullName == baseVmt[i].FullName).ToArray();
		var overridden = Enumerable.Range(0, baseVmt.Length).Except(inherited).ToArray();

		Assert.Multiple(() => {
			Assert.That(inherited, Is.Not.Empty);
			Assert.That(overridden, Is.Not.Empty);
			Assert.That(Marshal.ReadIntPtr(cube), Is.EqualTo(IntPtr.Zero), "parent, before init");
			Assert.That(inherited.Select(i => SlotOf(cube, i)), Is.All.EqualTo(IntPtr.Zero), "inherited slots, before init");
		});

		var init = Marshal.GetDelegateForFunctionPointer<Action>(NativeLibrary.GetExport(cubelibrary, "cubelib$init$"));
		init();

		Assert.Multiple(() => {
			Assert.That(Marshal.ReadIntPtr(cube), Is.EqualTo(square), "parent");
			foreach (var i in inherited) {
				Assert.That(SlotOf(cube, i), Is.EqualTo(SlotOf(square, i)).And.Not.EqualTo(IntPtr.Zero), $"inherited slot {i} ({baseVmt[i].Name})");
			}
			foreach (var i in overridden) {
				Assert.That(SlotOf(cube, i), Is.Not.EqualTo(SlotOf(square, i)), $"overridden slot {i} ({baseVmt[i].Name})");
			}
		});
	}

	[Test]
	public void CheckExternalBaseClasses()
	{
		BuildCubeLibrary();
		using var stream = File.OpenRead(Path.Combine(TempFolder, "cubelib.dll"));
		var context = new MetadataContext(stream) {
			ImportResolver = (library, ns, type) => library == "shapes"
				? _library.Types.Single(t => t.Name == type)
				: throw new InvalidOperationException($"Unexpected library '{library}'"),
		};
		var tri = (IClassType)new ExternalLibrary(context, "cubelib").Types.Single(t => t.Name == "Cube");

		Assert.Multiple(() => {
			Assert.That(tri.BaseType, Is.InstanceOf<ExternalClass>().With.Property("Name").EqualTo("Square"));
			Assert.That(((ExternalClass)tri.BaseType!).LibraryName, Is.EqualTo("shapes"));
			Assert.That(tri.Vmt, Has.Length.EqualTo(Class("Square").Vmt.Length));
		});
	}

	[Test]
	public void ExternalAbstractClassIsStillAbstract() => AssertCompileError("""
		var s = Shape.Create()
		""",
		"Cannot construct 'Shape': it is abstract");

	[Test]
	public void ExternalAbstractConstantThroughClassNameIsRefused() => AssertCompileError("""
		print Shape.Id.ToString()
		""",
		"Cannot read abstract member 'Shape.Id'");
}
