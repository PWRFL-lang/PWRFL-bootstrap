namespace PWR.Compiler.Test.Bootstrap;

// Testing that invalid class-related code produces the expected errors
internal class Phase4ClassErrors : Phase4
{
	[Test]
	public void ClassOverrideMissingKeyword() => RunError("""
		class Base
			def virtual Speak(): string
				return "base"
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end

			def Speak(): string
				return "derived"
			end
		end

		var d = Derived.Create()
		print d.Speak()
		""",
		"must be declared 'override'");

	[Test]
	public void ClassInheritanceCycle() => RunError("""
		class A extends B
			constructor Create()
				super Create()
			end
		end

		class B extends A
			constructor Create()
				super Create()
			end
		end

		var a = A.Create()
		""",
		"cannot inherit from itself");

	[Test]
	public void ClassOverrideWithNoVirtualBase() => RunError("""
		class Base
			constructor Create()
				super Create()
			end

			def Speak(): string
				return "base"
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end

			def override Speak(): string
				return "derived"
			end
		end

		var d = Derived.Create()
		print d.Speak()
		""",
		"no matching virtual member to override");

	[Test]
	public void ClassDuplicateMemberName() => RunError("""
		class Foo
			constructor Create()
				super Create()
			end

			constructor Create(x: int)
				super Create()
			end
		end

		var f = Foo.Create()
		""",
		"declared more than once");

	[Test]
	public void ClassBareConstructionUnsupported() => RunError("""
		class Foo
			constructor Create()
				super Create()
			end
		end

		var f = Foo()
		""",
		"must be constructed via a named constructor");

	[Test]
	public void ClassConstructorsAreNotInherited() => RunError("""
		class Animal
			constructor Create()
				super Create()
			end
		end

		class Dog extends Animal
			constructor Make()
				super Create()
			end
		end

		var d = Dog.Create()
		""",
		"does not contain a member named 'Create'");

	// Field default initializers are for classes and modules, not structs
	[Test]
	public void StructFieldDefaultRejected() => RunError("""
		struct S
			X: int = 1
		end
		""",
		"struct fields");

	[Test]
	public void ClassAbstractMemberNeedsAnAbstractClass() => RunError("""
		class Shape
			constructor Create()
				super Create()
			end

			def abstract Area(): int
		end
		""",
		"'Area' is abstract, but class 'Shape' is not declared abstract");

	[Test]
	public void ClassMustImplementInheritedAbstractMember() => RunError("""
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
		end
		""",
		"Class 'Square' is not abstract, but does not implement the abstract member 'Shape.Area'");

	[Test]
	public void ClassAbstractMetaclassExpressionIsError() => RunError("""
		class abstract Shape
			constructor Create()
				super Create()
			end
		end

		var m = meta Shape
		""",
		"Cannot take the metaclass of abstract class 'Shape'");

	[Test]
	public void ClassAbstractCannotBeConstructed() => RunError("""
		class abstract Shape
			constructor Create()
				super Create()
			end
		end

		var s = Shape.Create()
		""",
		"Cannot construct abstract class 'Shape'");

	[Test]
	public void ClassAbstractClassScopeMethodThroughClassName() => RunError("""
		class abstract Plugin
			constructor Create()
				super Create()
			end

			def class abstract Id(): int
		end

		print Plugin.Id().ToString()
		""",
		"Cannot call abstract method 'Plugin.Id'");

	[Test]
	public void ClassAbstractMethodThroughClassName() => RunError("""
		class abstract Shape
			constructor Create()
				super Create()
			end

			def abstract Area(): int
		end

		print Shape.Area().ToString()
		""",
		"Cannot call abstract method 'Shape.Area'");

	[Test]
	public void ClassAbstractMethodThroughSuper() => RunError("""
		class abstract A
			constructor Create()
				super Create()
			end

			def abstract F(): int
		end

		class B extends A
			constructor Create()
				super Create()
			end

			def override F(): int
				return super F()
			end
		end
		""",
		"Cannot call 'F' through 'super': it is abstract in 'A'");

	[Test]
	public void ClassAbstractGoesAfterClass() => RunParseError("""
		abstract class Shape
			constructor Create()
				super Create()
			end
		end
		""",
		"'abstract' goes after 'class'");

	[Test]
	public void ClassConstantThroughClassName() => RunTest("""
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

		class Triangle extends Shape
			constructor Create()
				super Create()
			end

			const class override Sides = 3
		end

		print Shape.Sides.ToString()
		print Square.Sides.ToString()
		print Triangle.Sides.ToString()
		""",
		"""
		0
		4
		3

		""");
}
