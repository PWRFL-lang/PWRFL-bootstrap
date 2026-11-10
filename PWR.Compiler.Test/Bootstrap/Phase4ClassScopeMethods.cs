namespace PWR.Compiler.Test.Bootstrap;

internal class Phase4ClassScopeMethods : Phase4
{
	[Test]
	public void ClassScopeAndInstanceVirtualMethods() => RunTest("""
		class Animal
			constructor Create()
				super Create()
			end

			def class Kind(): string
				return "animal"
			end

			def virtual Sound(): string
				return "..."
			end
		end

		class Dog extends Animal
			constructor Create()
				super Create()
			end

			def class override Kind(): string
				return "dog"
			end

			def override Sound(): string
				return "woof"
			end
		end

		def KindOf(cls: meta Animal): string
			return cls.Kind()
		end

		var a: Animal = Dog.Create()
		print a.Sound()
		print KindOf(meta Dog)
		print KindOf(meta Animal)
		print a.Kind()
		""",
		"""
		woof
		dog
		animal
		dog

		""");

	[Test]
	public void ClassScopeMethodIsInherited() => RunTest("""
		class Animal
			constructor Create()
				super Create()
			end

			def class Kind(): string
				return "animal"
			end
		end

		class Cat extends Animal
			constructor Create()
				super Create()
			end
		end

		let m: meta Animal = meta Cat
		print Cat.Kind()
		print m.Kind()
		""",
		"""
		animal
		animal

		""");

	[Test]
	public void ClassScopeMethodSelfIsTheMetaclass() => RunTest("""
		class Animal
			constructor virtual Create()
				super Create()
			end

			def class Kind(): string
				return "animal"
			end

			def class Twice(): string
				return Kind()
			end

			def class Make(): Animal
				return self.Create()
			end

			def virtual Name(): string
				return Kind()
			end
		end

		class Dog extends Animal
			constructor override Create()
				super Create()
			end

			def class override Kind(): string
				return "dog"
			end
		end

		print Dog.Twice()
		var d = Dog.Make()
		print d.Name()
		""",
		"""
		dog
		dog

		""");

	[Test]
	public void ClassScopeMethodMayBeCalledBeforeSuper() => RunTest("""
		class Foo
			constructor Create()
				Announce()
				super Create()
			end

			def class Announce()
				Console.PrintLn("created")
			end
		end

		Foo.Create()
		""",
		"""
		created

		""");

	[Test]
	public void ClassScopeModifiersMayComeInEitherOrder() => RunTest("""
		class Base
			constructor Create()
				super Create()
			end

			def class Describe(): string
				return "base"
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end

			def override class Describe(): string
				return "derived"
			end
		end

		print Derived.Describe()
		""",
		"""
		derived

		""");

	[Test]
	public void ClassScopeMethodCannotUseInstanceMembers() => RunError("""
		class Foo
			X: int

			constructor Create()
				super Create()
				X = 1
			end

			def class Bad(): int
				return X
			end
		end
		""",
		"is an instance member, and cannot be used from a class-scope method");

	[Test]
	public void ClassScopeOverrideMissingKeyword() => RunError("""
		class Base
			constructor Create()
				super Create()
			end

			def class Describe(): string
				return "base"
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end

			def class Describe(): string
				return "derived"
			end
		end
		""",
		"must be declared 'override'");

	[Test]
	public void ClassScopeCannotOverrideAnInstanceMethod() => RunError("""
		class Base
			constructor Create()
				super Create()
			end

			def virtual Describe(): string
				return "base"
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end

			def class override Describe(): string
				return "derived"
			end
		end
		""",
		"is a class-scope method, but the member it overrides is an instance method");

	[Test]
	public void ClassScopeOnlyInClasses() => RunError("""
		struct Point
			X: int

			def class Origin(): int
				return 0
			end
		end
		""",
		"Only a method of a class can be declared 'class'");

	[Test]
	public void ClassConstructorCannotBeClassScope() => RunParseError("""
		class Foo
			constructor class Create()
				super Create()
			end
		end
		""",
		"A constructor cannot be declared 'class'");

	[Test]
	public void ClassModifierRepeated() => RunParseError("""
		class Foo
			def class class Describe(): string
				return "x"
			end
		end
		""",
		"appears more than once");

	[Test]
	public void ClassVirtualAndOverrideTogether() => RunParseError("""
		class Foo
			def virtual override Describe(): string
				return "x"
			end
		end
		""",
		"cannot be both 'virtual' and 'override'");

	[Test]
	public void ClassScopeMethodCanCallSuper() => RunTest("""
		class Base
			constructor Create()
				super Create()
			end

			def class Kind(): string
				return "base"
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end

			def class override Kind(): string
				return super Kind()
			end
		end

		print Derived.Kind()
		""",
		"""
		base

		""");
}
