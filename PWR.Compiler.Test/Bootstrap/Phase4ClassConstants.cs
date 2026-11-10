namespace PWR.Compiler.Test.Bootstrap;

internal class Phase4ClassConstants : Phase4
{
	[Test]
	public void ClassConstantIsInScopeForSubclasses() => RunTest("""
		class Animal
			constructor virtual Create()
				super Create()
			end

			const class Sound: string = "..."

			def class Kind(): string
				return "animal"
			end
		end

		class Dog extends Animal
			constructor override Create()
				super Create()
			end
		end

		print Dog.Kind()
		print Dog.Sound
		""",
		"""
		animal
		...

		""");

	[Test]
	public void ClassConstantThroughMetaValueAndInstance() => RunTest("""
		class Shape
			constructor Create()
				super Create()
			end

			const class Sides: int = 0
		end

		class Square extends Shape
			constructor Create()
				super Create()
			end

			const class override Sides = 4
		end

		class Cube extends Square
			constructor Create()
				super Create()
			end
		end

		def SidesOf(cls: meta Shape): int
			return cls.Sides
		end

		let s: Shape = Cube.Create()
		print SidesOf(meta Shape).ToString()
		print SidesOf(meta Cube).ToString()
		print Cube.Sides.ToString()
		print s.Sides.ToString()
		""",
		"""
		0
		4
		4
		4

		""");

	[Test]
	public void ClassConstantByBareName() => RunTest("""
		class Shape
			constructor Create()
				super Create()
			end

			const class Sides: int = 0
			const class Label: string = "shape"

			def Corners(): int
				return Sides
			end

			def class Name(): string
				return Label
			end
		end

		class Square extends Shape
			constructor Create()
				super Create()
			end

			const class override Sides = 4
			const class override Label = "square"
		end

		let s: Shape = Square.Create()
		print s.Corners().ToString()
		print Square.Name()
		print Shape.Name()
		""",
		"""
		4
		square
		shape

		""");

	[Test]
	public void ClassConstantTypes() => RunTest("""
		class Config
			constructor Create()
				super Create()
			end

			const class Enabled: bool = false
			const class Letter: char = 'C'
			const class Code: byte = 7
			const class Title: string = "cfg"
			const class Nothing: ptr = nil
		end

		class Special extends Config
			constructor Create()
				super Create()
			end

			const class override Enabled = true
			const class override Letter = 'S'
			const class override Code = 9
			const class override Title = "special"
		end

		def Show(cls: meta Config)
			print (cls.Enabled.ToString())
			print ((cls.Letter == 'S').ToString())
			print (cls.Code.ToString())
			print cls.Title
		end

		Show(meta Config)
		Show(meta Special)
		""",
		"""
		false
		false
		7
		cfg
		true
		true
		9
		special

		""");

	[Test]
	public void ClassConstantValueMayBeFolded() => RunTest("""
		class Shape
			constructor Create()
				super Create()
			end

			const class Area: int = 2 * 3
			const class Offset: int = -4
		end

		print Shape.Area.ToString()
		print Shape.Offset.ToString()
		""",
		"""
		6
		-4

		""");

	[Test]
	public void ClassConstantHoldsMetaclass() => RunTest("""
		class Shape
			constructor virtual Create()
				super Create()
			end

			const class Default: meta Shape = meta Square

			def virtual Name(): string
				return "shape"
			end
		end

		class Square extends Shape
			constructor override Create()
				super Create()
			end

			def override Name(): string
				return "square"
			end
		end

		def Make(): Shape
			return Shape.Default.Create()
		end

		print Make().Name()
		""",
		"""
		square

		""");

	[Test]
	public void ClassAbstractConstant() => RunTest("""
		class abstract Plugin
			constructor virtual Create()
				super Create()
			end

			const class abstract Id: int
		end

		class Echo extends Plugin
			constructor override Create()
				super Create()
			end

			const class override Id = 7
		end

		def IdOf(cls: meta Plugin): int
			return cls.Id
		end

		let p: Plugin = Echo.Create()
		print IdOf(meta Echo).ToString()
		print Echo.Id.ToString()
		print p.Id.ToString()
		""",
		"""
		7
		7
		7

		""");

	[Test]
	public void ClassConstantMayBeReadBeforeSuper() => RunTest("""
		class Foo
			const class Tag: int = 5

			constructor Create()
				print Tag.ToString()
				super Create()
			end
		end

		Foo.Create()
		""",
		"""
		5

		""");

	// This should be valid, but is not implemented yet in Phase 4
	[Test]
	public void ClassConstantValueMustBeConstant() => RunError("""
		class Shape
			const class A: int = 1
			const class B: int = A * 2
		end
		""",
		"has to be known to the compiler");

	[Test]
	public void ClassConstantNeedsValue() => RunError("""
		class Shape
			const class A: int
		end
		""",
		"'A' needs a value");

	[Test]
	public void ClassConstantNeedsType() => RunError("""
		class Shape
			const class A = 5
		end
		""",
		"'A' needs a type");

	[Test]
	public void ClassConstantValueMustMatchItsType() => RunError("""
		class Shape
			const class A: int = "four"
		end
		""",
		"cannot be the value of the constant 'A'");

	[Test]
	public void ClassAbstractConstantCannotHaveValue() => RunError("""
		class abstract Plugin
			const class abstract Id: int = 5
		end
		""",
		"'Id' is abstract, so it can't have a value");

	[Test]
	public void ClassAbstractConstantNeedsAnAbstractClass() => RunError("""
		class Plugin
			const class abstract Id: int
		end
		""",
		"'Id' is abstract, but class 'Plugin' is not declared abstract");

	[Test]
	public void ClassMustImplementInheritedAbstractConstant() => RunError("""
		class abstract Plugin
			const class abstract Id: int
		end

		class Echo extends Plugin
		end
		""",
		"Class 'Echo' is not abstract, but does not implement the abstract member 'Id' of 'Plugin'");

	[Test]
	public void ClassAbstractConstantThroughClassName() => RunError("""
		class abstract Plugin
			const class abstract Id: int
		end

		print Plugin.Id.ToString()
		""",
		"Cannot read abstract member 'Plugin.Id'");

	[Test]
	public void ClassConstantOverrideWithoutBase() => RunError("""
		class Shape
			const class override Sides = 4
		end
		""",
		"there is no matching virtual member to override");

	[Test]
	public void ClassConstantOverrideKeepsTheType() => RunError("""
		class Shape
			const class Sides: int = 0
		end

		class Square extends Shape
			const class override Sides: string = "four"
		end
		""",
		"has type 'string', but the constant it overrides has type 'int'");

	[Test]
	public void ClassConstantMissingOverrideKeyword() => RunError("""
		class Shape
			const class Sides: int = 0
		end

		class Square extends Shape
			const class Sides: int = 4
		end
		""",
		"must be declared 'override'");

	[Test]
	public void ClassConstantCannotOverrideAMethod() => RunError("""
		class Shape
			def class Kind(): string
				return "shape"
			end
		end

		class Square extends Shape
			const class override Kind = "square"
		end
		""",
		"is a constant, but the member it overrides is a class-scope method");

	[Test]
	public void ClassConstantCannotBeAssigned() => RunError("""
		class Shape
			const class Sides: int = 0
		end

		Shape.Sides = 5
		""",
		"'Sides' is a constant, and cannot be assigned to");

	[Test]
	public void ClassConstantOnlyInClasses() => RunError("""
		struct Point
			const class Dimensions: int = 2
		end
		""",
		"Only a class can declare a 'const class' member");

	[Test]
	public void ClassConstantAbstractNeedsClass() => RunParseError("""
		class abstract Plugin
			const abstract Id: int
		end
		""",
		"need 'class'");

	[Test]
	public void ClassConstantModifierRepeated() => RunParseError("""
		class Shape
			const class class Sides: int = 0
		end
		""",
		"appears more than once");

}
