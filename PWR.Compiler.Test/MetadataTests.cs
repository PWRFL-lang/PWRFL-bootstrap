using PWR.Compiler.Ast;
using PWR.Compiler.Metadata;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using PWR.Compiler.TypeSystem.External;

namespace PWR.Compiler.Test;

// Test a library's metadata, and whether the metadata reader can read it successfully.
// These tests build small libraries and examine the metadata they produce.
internal class MetadataTests
{
	private static string TempFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pwr");

	private static MetadataContext BuildLibrary(string name, string code)
	{
		Directory.CreateDirectory(TempFolder);
		var runtime = Directory.EnumerateFiles(".\\Code", "pwr*.pwrfl").OrderByDescending(f => f).First();
		var memory = Directory.EnumerateFiles(".\\Code", "mem*.pwrfl").OrderByDescending(f => f).First();
		var output = Path.Combine(TempFolder, name + ".dll");
		var result = new PwrCompilerP3().Compile(new CompileOptions(
			[CodeSource.FromFile(runtime), CodeSource.FromFile(memory), CodeSource.FromText(code)],
			output, [], ProjectType: ProjectType.Library, NoStdLib: true));
		Assert.That(result, Is.InstanceOf<BuildCompileResult>(), (result as ErrorCompileResult)?.Error);
		using var stream = File.OpenRead(output);
		return new MetadataContext(stream);
	}

	private static string TypeNameAt(MetadataContext context, Token owner)
		=> context.TypeDefinitionTable[owner.Value].GetName(context);

	[Test]
	public void CheckFields()
	{
		var context = BuildLibrary("metadata-fields-table", """
			struct Point
				X: int
				Y: int
			end

			class Foo
				A: int
				B: int

				constructor Create()
					super Create()
					A = 1
					B = 2
				end
			end

			module Config
				const Limit = 5
				let Size = 7
				Count: int = 0
			end
			""");

		var fieldsByOwner = context.FieldDefinitionTable
			.GroupBy(f => TypeNameAt(context, f.OwnerRef))
			.ToDictionary(g => g.Key, g => g.Select(f => context.GetString(f.NameRef)).ToArray());

		Assert.Multiple(() => {
			Assert.That(fieldsByOwner["Point"], Is.EqualTo([ "X", "Y" ]));
			Assert.That(fieldsByOwner["Foo"], Is.EqualTo([ "A", "B" ]));
			Assert.That(fieldsByOwner["Config"], Is.EqualTo([ "Limit", "Size", "Count" ]));
		});
	}

	[Test]
	public void FieldTableIsOrderedByOwner()
	{
		var context = BuildLibrary("metadata-fields-order", """
			struct First
				A: int
			end

			struct Second
				B: int
				C: int
			end

			module Third
				D: int = 0
			end
			""");

		var owners = context.FieldDefinitionTable.Select(f => f.OwnerRef.AsInt).ToArray();
		Assert.Multiple(() => { 
			Assert.That(owners, Is.Ordered);
			Assert.That(owners.Distinct().Count(), Is.GreaterThanOrEqualTo(4));
		});
	}

	[Test]
	public void ReadMetaclassTypes()
	{
		var context = BuildLibrary("metadata-meta-types", """
			class Registry
				Any: meta
				Own: meta Registry
			end

			module Passing
				def Through(m: meta): meta
					return m
				end
			end
			""");
		var library = new ExternalLibrary(context, "metadata-meta-types");
		var registry = library.Types.Single(t => t.Name == "Registry");
		var through = (ExternalMethod)library.Types.Single(t => t.Name == "Passing").GetMember("Through")!;

		Assert.Multiple(() => {
			Assert.That(registry.GetMember("Any")!.Type, Is.SameAs(MetaclassType.Any));
			Assert.That(registry.GetMember("Own")!.Type, Is.InstanceOf<MetaclassType>().With.Property("Class").SameAs(registry));
			Assert.That(through.Args[0].ParamType.Semantic!.Type, Is.SameAs(MetaclassType.Any));
			Assert.That(through.Type, Is.SameAs(MetaclassType.Any));
		});
	}

	[Test]
	public void ReadFieldTypes()
	{
		var context = BuildLibrary("metadata-fields-types", """
			struct Inner
				N: int
			end

			struct Holder
				Big: long
				Name: string
				Raw: ptr
				Bytes: byte span
				Link: Holder ref?
				Items: Inner[4]
				Single: Inner
			end
			""");
		var library = new ExternalLibrary(context, "metadata-fields-types");
		var inner = library.Types.Single(t => t.Name == "Inner");
		var holder = library.Types.Single(t => t.Name == "Holder");
		IType FieldType(string name) => holder.GetMember(name)!.Type;

		Assert.Multiple(() => {
			Assert.That(FieldType("Big"), Is.SameAs(Types.Int64));
			Assert.That(FieldType("Name"), Is.SameAs(Types.String));
			Assert.That(FieldType("Raw"), Is.SameAs(Types.Ptr));
			Assert.That(FieldType("Bytes"), Is.InstanceOf<SpanType>().With.Property("ElementType").SameAs(Types.Byte));
			Assert.That(FieldType("Single"), Is.SameAs(inner));
		});

		// A recursive type has to come back as itself, not as a copy.
		var link = FieldType("Link");
		Assert.That(link, Is.InstanceOf<NilableType>());
		var linkTarget = ((NilableType)link).BaseType;
		Assert.Multiple(() => {
			Assert.That(linkTarget, Is.InstanceOf<RefType>());
			Assert.That(((RefType)linkTarget).BaseType, Is.SameAs(holder));
		});

		var items = FieldType("Items");
		Assert.Multiple(() => {
			Assert.That(items, Is.InstanceOf<InlineArrayType>());
			Assert.That(((InlineArrayType)items).ElementType, Is.SameAs(inner));
			Assert.That(((InlineArrayType)items).Size, Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(4));
		});
	}

	private const string MixedProgram = """
		def Helper(a: int, b: int): int
			return a + b
		end

		struct Rect
			W: int

			def Scale(factor: int): int
				return W * factor
			end
		end

		def Other()
		end

		module Settings
			def Run()
			end
		end
		""";

	[Test]
	public void TopLevelFunctionsHaveNoOwner()
	{
		var context = BuildLibrary("metadata-methods-owner", MixedProgram);
		int OwnerOf(string method) => context.MethodDefinitionTable.Single(m => context.GetString(m.NameRef) == method).OwnerRef;

		Assert.Multiple(() => {
			Assert.That(OwnerOf("Helper"), Is.EqualTo(MetadataContext.NO_OWNER));
			Assert.That(OwnerOf("Other"), Is.EqualTo(MetadataContext.NO_OWNER));
			// the runtime's own top-level functions too
			Assert.That(OwnerOf("memcpy"), Is.EqualTo(MetadataContext.NO_OWNER));
			Assert.That(context.TypeDefinitionTable[OwnerOf("Scale")].GetName(context), Is.EqualTo("Rect"));
			Assert.That(context.TypeDefinitionTable[OwnerOf("Run")].GetName(context), Is.EqualTo("Settings"));
		});
	}

	[Test]
	public void MethodTableIsOrderedByOwner()
	{
		var context = BuildLibrary("metadata-methods-order", MixedProgram);
		Assert.That(context.MethodDefinitionTable.Select(m => m.OwnerRef), Is.Ordered);
	}

	[Test]
	public void TopLevelFunctionsAreNotMembersOfAnyType()
	{
		var library = new ExternalLibrary(BuildLibrary("metadata-methods-members", MixedProgram), "metadata-methods-members");
		var first = library.Types[0];

		Assert.Multiple(() => {
			Assert.That(first.Name, Is.EqualTo("Kernel32"));
			Assert.That(first.GetMember("VirtualAlloc"), Is.Not.Null);
			Assert.That(first.GetMember("memcpy"), Is.Null);
		});
	}

	[Test]
	public void ParametersStayWithTheirMethods()
	{
		var library = new ExternalLibrary(BuildLibrary("metadata-methods-params", MixedProgram), "metadata-methods-params");
		var scale = (ExternalMethod)library.Types.Single(t => t.Name == "Rect").GetMember("Scale")!;
		var names = scale.Args.Select(p => p.Name.Name).ToArray();

		Assert.That(names, Does.Contain("factor"));
		Assert.That(names, Does.Not.Contain("a").And.Not.Contain("b"));
	}

	private static void AssertFlags<T>(T flags, T expected, T forbidden = default) where T : struct, Enum
	{
		Assert.That(flags.HasFlag(expected), Is.True, $"expected {expected}, got {flags}");
		if (!EqualityComparer<T>.Default.Equals(forbidden, default)) {
			Assert.That(flags.HasFlag(forbidden), Is.False, $"expected no {forbidden}, got {flags}");
		}
	}

	private static int RowOfType(MetadataContext context, string name)
		=> context.TypeDefinitionTable.FindIndex(t => t.GetName(context) == name);

	[Test]
	public void MethodAttributesAndSlotsAreRecorded()
	{
		var context = BuildLibrary("metadata-methods-vmt", """
			class Base
				constructor virtual Create()
					super Create()
				end

				def virtual Speak(): string
					return "base"
				end

				def class Kind(): string
					return "base"
				end

				def Plain(): int
					return 1
				end
			end

			class Derived extends Base
				constructor override Create()
					super Create()
				end

				def override Speak(): string
					return "derived"
				end
			end

			def Helper()
			end
			""");
		MethodDefinition Method(string type, string name) => context.MethodDefinitionTable.Single(
			m => m.OwnerRef >= 0 && context.TypeDefinitionTable[m.OwnerRef].GetName(context) == type && context.GetString(m.NameRef) == name);

		var baseCreate = Method("Base", "Create");
		var baseSpeak = Method("Base", "Speak");
		var baseKind = Method("Base", "Kind");
		var basePlain = Method("Base", "Plain");
		var derivedCreate = Method("Derived", "Create");
		var derivedSpeak = Method("Derived", "Speak");
		var helper = context.MethodDefinitionTable.Single(m => context.GetString(m.NameRef) == "Helper");

		Assert.Multiple(() => {
			// slots are handed out in declaration order
			Assert.That(baseCreate.Slot, Is.EqualTo(0));
			AssertFlags(baseCreate.Flags, MethodAttributes.Constructor | MethodAttributes.Virtual);
			Assert.That(baseSpeak.Slot, Is.EqualTo(1));
			AssertFlags(baseSpeak.Flags, MethodAttributes.Virtual, MethodAttributes.ClassScope);
			Assert.That(baseKind.Slot, Is.EqualTo(2));
			AssertFlags(baseKind.Flags, MethodAttributes.ClassScope);
			// an override reuses the slot it overrides
			Assert.That(derivedCreate.Slot, Is.EqualTo(0));
			AssertFlags(derivedCreate.Flags, MethodAttributes.Constructor | MethodAttributes.Override);
			Assert.That(derivedSpeak.Slot, Is.EqualTo(1));
			AssertFlags(derivedSpeak.Flags, MethodAttributes.Override);
			// non-virtual members have no slot
			Assert.That(basePlain.Slot, Is.EqualTo(MetadataContext.NO_SLOT));
			AssertFlags(basePlain.Flags, MethodAttributes.None, MethodAttributes.Virtual);
			Assert.That(helper.Slot, Is.EqualTo(MetadataContext.NO_SLOT));
		});
	}

	[Test]
	public void BaseClassIsRecorded()
	{
		var context = BuildLibrary("metadata-base-class", """
			class Animal
				constructor Create()
					super Create()
				end
			end

			class Dog extends Animal
				constructor Create()
					super Create()
				end
			end
			""");
		var animal = RowOfType(context, "Animal");
		var dog = RowOfType(context, "Dog");
		Assert.That(context.TypeDefinitionTable[animal].AssociatedTypeRef, Is.EqualTo(0));

		var blob = context.GetBlob(context.TypeDefinitionTable[dog].AssociatedTypeRef);
		Assert.Multiple(() => {
			Assert.That(blob[0], Is.EqualTo((byte)SigType.Field));
			Assert.That(blob[1], Is.EqualTo((byte)SigElement.Class));
			Assert.That(BitConverter.ToInt32(blob, 2), Is.EqualTo(new Token(MetadataContext.TYPE_DEF_ID, animal).AsInt));
		});
	}

	private const string ConstantsProgram = """
		module Limits
			const Limit = 5
			const Label = "ok"
			const Enabled = true
			let Size = 7
			Count: int = 0
		end
		""";

	[Test]
	public void ConstantFieldsRecordTheirValues()
	{
		var context = BuildLibrary("metadata-constants", ConstantsProgram);
		FieldDefinition Field(string name) => context.FieldDefinitionTable.Single(f => context.GetString(f.NameRef) == name);
		Expression ValueOf(FieldDefinition f) => Signatures.ReadConstant(context.GetBlob(f.ValueRef));

		var limit = Field("Limit");
		var label = Field("Label");
		var enabled = Field("Enabled");
		var size = Field("Size");
		var count = Field("Count");

		Assert.Multiple(() => {
			AssertFlags(limit.Flags, FieldFlags.Const);
			Assert.That(ValueOf(limit), Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(5));
			AssertFlags(label.Flags, FieldFlags.Const);
			Assert.That(ValueOf(label), Is.InstanceOf<StringLiteralExpression>().With.Property("Value").EqualTo("ok"));
			AssertFlags(enabled.Flags, FieldFlags.Const);
			Assert.That(ValueOf(enabled), Is.InstanceOf<BooleanLiteralExpression>().With.Property("Value").True);
			// `let` and plain fields aren't constants, and have no recorded value
			AssertFlags(size.Flags, FieldFlags.None, FieldFlags.Const);
			Assert.That(size.ValueRef, Is.EqualTo(0));
			AssertFlags(count.Flags, FieldFlags.None, FieldFlags.Const);
			Assert.That(count.ValueRef, Is.EqualTo(0));
			// none of them are in a VMT
			Assert.That(new[] { limit, label, enabled, size, count }.Select(f => f.Slot), Has.All.EqualTo(MetadataContext.NO_SLOT));
		});
	}

	[Test]
	public void ExternalFieldsCarryTheirConstants()
	{
		var library = new ExternalLibrary(BuildLibrary("metadata-constants-read", ConstantsProgram), "metadata-constants-read");
		var limits = library.Types.Single(t => t.Name == "Limits");
		Expression? ConstantOf(string name) => ((ExternalField)limits.GetMember(name)!).Constant;

		Assert.Multiple(() => {
			Assert.That(ConstantOf("Limit"), Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(5));
			Assert.That(ConstantOf("Label"), Is.InstanceOf<StringLiteralExpression>().With.Property("Value").EqualTo("ok"));
			Assert.That(ConstantOf("Enabled"), Is.InstanceOf<BooleanLiteralExpression>().With.Property("Value").True);
			Assert.That(ConstantOf("Size"), Is.Null);
			Assert.That(ConstantOf("Count"), Is.Null);
		});
	}

	[Test]
	public void BoolParameterDefaultIsRecorded()
	{
		var context = BuildLibrary("metadata-bool-default", """
			def Flip(enabledFlag: bool = true): bool
				return enabledFlag
			end
			""");
		var param = context.ParamDefinitionTable.Single(p => context.GetString(p.NameRef) == "enabledFlag");

		Assert.Multiple(() => {
			Assert.That(param.DefaultRef, Is.Not.EqualTo(0));
			Assert.That(Signatures.ReadConstant(context.GetBlob(param.DefaultRef)),
				Is.InstanceOf<BooleanLiteralExpression>().With.Property("Value").True);
		});
	}

	[Test]
	public void AbstractClassesAndMembersAreFlagged()
	{
		var context = BuildLibrary("metadata-abstract", """
			class abstract Shape
				constructor Create()
					super Create()
				end

				def abstract Area(): int
			end

			class Square extends Shape
				constructor Create()
					super Create()
				end

				def override Area(): int
					return 1
				end
			end
			""");
		var shape = context.TypeDefinitionTable[RowOfType(context, "Shape")];
		var square = context.TypeDefinitionTable[RowOfType(context, "Square")];
		MethodDefinition Area(string type) => context.MethodDefinitionTable.Single(
			m => m.OwnerRef >= 0 && context.TypeDefinitionTable[m.OwnerRef].GetName(context) == type && context.GetString(m.NameRef) == "Area");

		Assert.Multiple(() => {
			AssertFlags(shape.Flags, TypeFlags.Abstract);
			AssertFlags(square.Flags, TypeFlags.None, TypeFlags.Abstract);
			AssertFlags(Area("Shape").Flags, MethodAttributes.Abstract);
			Assert.That(Area("Shape").Slot, Is.EqualTo(0));
			AssertFlags(Area("Square").Flags, MethodAttributes.Override, MethodAttributes.Abstract);
			Assert.That(Area("Square").Slot, Is.EqualTo(0));
		});
	}

	[Test]
	public void ClassConstantsAreRecorded()
	{
		var context = BuildLibrary("metadata-class-constants", """
			class abstract Plugin
				constructor virtual Create()
					super Create()
				end

				def class Tag(): int
					return 1
				end

				const class Id: int = 5
				const class Label: string = "p"
				const class Default: meta Plugin = meta Echo
				const class abstract Code: int
			end

			class Echo extends Plugin
				constructor override Create()
					super Create()
				end

				const class override Id = 7
				const class override Code = 3
			end
			""");
		var plugin = RowOfType(context, "Plugin");
		var echo = RowOfType(context, "Echo");
		FieldDefinition Field(int owner, string name) => context.FieldDefinitionTable.Single(
			f => f.OwnerRef.Value == owner && context.GetString(f.NameRef) == name);
		Expression ValueOf(FieldDefinition f) => Signatures.ReadConstant(context.GetBlob(f.ValueRef));

		var id = Field(plugin, "Id");
		var label = Field(plugin, "Label");
		var @default = Field(plugin, "Default");
		var code = Field(plugin, "Code");
		var echoId = Field(echo, "Id");
		var echoCode = Field(echo, "Code");

		Assert.Multiple(() => {
			Assert.That(id.Slot, Is.EqualTo(2));
			AssertFlags(id.Flags, FieldFlags.Const | FieldFlags.VirtualConstant, FieldFlags.Abstract | FieldFlags.Override);
			Assert.That(ValueOf(id), Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(5));
			Assert.That(label.Slot, Is.EqualTo(3));
			Assert.That(ValueOf(label), Is.InstanceOf<StringLiteralExpression>().With.Property("Value").EqualTo("p"));
			Assert.That(@default.Slot, Is.EqualTo(4));
			Assert.That(code.Slot, Is.EqualTo(5));
			// an abstract constant has no value
			AssertFlags(code.Flags, FieldFlags.VirtualConstant | FieldFlags.Abstract);
			Assert.That(code.ValueRef, Is.EqualTo(0));
			// an override reuses the slot it overrides
			Assert.That(echoId.Slot, Is.EqualTo(2));
			AssertFlags(echoId.Flags, FieldFlags.VirtualConstant | FieldFlags.Override, FieldFlags.Abstract);
			Assert.That(ValueOf(echoId), Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(7));
			Assert.That(echoCode.Slot, Is.EqualTo(5));
			Assert.That(ValueOf(echoCode), Is.InstanceOf<IntegerLiteralExpression>().With.Property("Value").EqualTo(3));
			Assert.That(context.TypeDefinitionTable[plugin].VmtSlotCount, Is.EqualTo(6));
		});

		var blob = context.GetBlob(@default.ValueRef);
		Assert.Multiple(() => {
			Assert.That(blob[0], Is.EqualTo((byte)SigElement.Meta));
			Assert.That(BitConverter.ToInt32(blob, 1), Is.EqualTo(new Token(MetadataContext.TYPE_DEF_ID, echo).AsInt));
		});
	}
}
