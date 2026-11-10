namespace PWR.Compiler.Test.Bootstrap;

internal class Phase4Classes : Phase4
{
	[Test]
	public void ClassBasicInstantiation() => RunTest("""
		class Counter
			value: int

			constructor Create(initial: int)
				super Create()
				value = initial
			end

			def Increment()
				value += 1
			end

			def GetValue(): int
				return value
			end
		end

		var c = Counter.Create(0)
		c.Increment()
		c.Increment()
		c.Increment()
		print c.GetValue().ToString()  ; 3
		""",
		"""
		3

		""");

	[Test]
	public void ClassInheritanceAndOverride() => RunTest("""
		class Animal
			Name: string

			constructor Create(name: string)
				super Create()
				Name = name
			end

			def virtual Speak(): string
				return Name + " makes a sound"
			end
		end

		class Cat extends Animal
			constructor Create(name: string)
				super Create(name)
			end

			def override Speak(): string
				return Name + " mews"
			end
		end

		let c: Animal = Cat.Create("Leo")
		print c.Speak()
		""",
		"""
		Leo mews

		""");

	[Test]
	public void ClassDerivedFieldLayout() => RunTest("""
		class Base
			A: int

			constructor Create(a: int)
				super Create()
				A = a
			end

			def GetA(): int
				return A
			end
		end

		class Derived extends Base
			B: int

			constructor Create(a: int, b: int)
				super Create(a)
				B = b
			end

			def GetB(): int
				return B
			end
		end

		var d = Derived.Create(3, 4)
		var b: Base = d
		print b.GetA().ToString()
		print d.GetB().ToString()
		""",
		"""
		3
		4

		""");

	[Test]
	public void ClassVirtualDispatchMultiLevel() => RunTest("""
		class Base
			constructor Create()
				super Create()
			end

			def virtual A(): int
				return 1
			end

			def virtual B(): int
				return 2
			end

			def SumAB(): int
				return A() + B()
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end

			def override A(): int
				return 10
			end

			def virtual C(): int
				return 3
			end
		end

		class Leaf extends Derived
			constructor Create()
				super Create()
			end

			def override C(): int
				return 30
			end
		end

		var l = Leaf.Create()
		var b: Base = l
		var d: Derived = l
		print b.A().ToString()
		print b.B().ToString()
		print d.C().ToString()
		print b.SumAB().ToString()
		var plain = Base.Create()
		print plain.A().ToString()
		""",
		"""
		10
		2
		30
		12
		1

		""");

	[Test]
	// Tests that the compiler can create and statically call class-scope virtual methods.
	// Testing virtual dispatch on class-scope methods comes later.
	public void ClassScopeVirtualMethod() => RunTest("""
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

			def class override Describe(): string
				return "derived"
			end
		end

		print Base.Describe()
		print Derived.Describe()
		""",
		"""
		base
		derived

		""");

	[Test]
	public void ClassConstructorChaining() => RunTest("""
		class Point
			X: int
			Y: int

			constructor Create(x: int, y: int)
				super Create()
				X = x
				Y = y
			end

			constructor Origin()
				Create(0, 0)
			end
		end

		var p = Point.Origin()
		print ((p.X + p.Y).ToString())
		""",
		"""
		0

		""");

	[Test]
	public void ClassFieldDefaultsSatisfyDefiniteAssignment() => RunTest("""
		class Config
			Retries: int = 3
			Verbose: bool = false

			constructor Create()
				super Create()
			end
		end

		var c = Config.Create()
		print c.Retries.ToString()
		""",
		"""
		3

		""");

	[Test]
	public void ClassSuperCallInOverride() => RunTest("""
		class Base
			constructor Create()
				super Create()
			end

			def virtual Bonus(): int
				return 10
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end

			def override Bonus(): int
				return super Bonus() + 5
			end
		end

		var d = Derived.Create()
		print d.Bonus().ToString()
		""",
		"""
		15

		""");

	[Test]
	public void ClassAssignmentIsReferenceNotCopy() => RunTest("""
		class Counter
			value: int

			constructor Create(initial: int)
				super Create()
				value = initial
			end

			def Increment()
				value += 1
			end

			def GetValue(): int
				return value
			end
		end

		var a = Counter.Create(0)
		var b = a
		b.Increment()
		b.Increment()
		print a.GetValue().ToString()
		""",
		"""
		2

		""");

	[Test]
	public void ClassPassedByReferenceToFunction() => RunTest("""
		class Counter
			value: int

			constructor Create(initial: int)
				super Create()
				value = initial
			end

			def Increment()
				value += 1
			end

			def GetValue(): int
				return value
			end
		end

		def Bump(c: Counter)
			c.Increment()
		end

		var a = Counter.Create(0)
		Bump(a)
		Bump(a)
		Bump(a)
		print a.GetValue().ToString()
		""",
		"""
		3

		""");
	private const string Animals = """
		class Animal
			Legs: int = 4

			constructor virtual Create()
				super Create()
			end

			def Describe(): string
				return "animal"
			end

			def virtual Speak(): string
				return "..."
			end

			def Legged(): int
				return Legs
			end
		end

		class Dog extends Animal
			constructor override Create()
				super Create()
			end

			def override Speak(): string
				return "woof"
			end
		end

		class Puppy extends Dog
			constructor override Create()
				super Create()
			end
		end

		""";

	[Test]
	public void ClassInheritedMethodsAndFields() => RunTest(Animals + """
		var d = Dog.Create()
		print d.Describe()
		print d.Speak()
		print d.Legs.ToString()
		d.Legs = 3
		print d.Legs.ToString()
		print d.Legged().ToString()
		""",
		"""
		animal
		woof
		4
		3
		3

		""");

	[Test]
	public void ClassMultiLevelInheritance() => RunTest(Animals + """
		let p = Puppy.Create()
		print p.Describe()
		print p.Speak()
		print p.Legs.ToString()
		let a: Animal = p
		print a.Speak()
		""",
		"""
		animal
		woof
		4
		woof

		""");

	[Test]
	public void ClassMetaValueInstanceMembersAreNotInScope() => RunError(Animals + """
		def Make(cls: meta Dog): string
			return cls.Describe()
		end
		""",
		"does not contain a member named 'Describe'");

	[Test]
	public void ClassInheritedMembersAreInScope() => RunTest("""
		class Animal
			Legs: int = 4

			constructor virtual Create()
				super Create()
			end

			def Describe(): string
				return "animal"
			end
		end

		class Bird extends Animal
			constructor override Create()
				super Create()
				self.Legs = 2
			end

			def Report(): string
				return self.Describe() + " with " + self.Legs.ToString() + " legs"
			end
		end

		let b = Bird.Create()
		print b.Report()
		""",
		"""
		animal with 2 legs

		""");

	[Test]
	public void ClassSuperCallMembersAreInScope() => RunTest("""
		class Animal
			constructor virtual Create()
				super Create()
			end

			def virtual Speak(): string
				return "..."
			end
		end

		class Dog extends Animal
			constructor override Create()
				super Create()
			end
		end

		class Puppy extends Dog
			constructor override Create()
				super Create()
			end

			def override Speak(): string
				return "yip after " + super Speak()
			end
		end

		var p = Puppy.Create()
		print p.Speak()
		""",
		"""
		yip after ...

		""");

	[Test]
	public void ClassFieldIsInScope() => RunTest("""
		class Counter
			Count: int = 1

			constructor Create()
				super Create()
			end
		end

		var c = Counter.Create()
		var d = Counter.Create()
		c.Count = 5
		print c.Count.ToString()
		c.Count += 2
		print c.Count.ToString()
		print d.Count.ToString()
		""",
		"""
		5
		7
		1

		""");

	[Test]
	public void ClassFieldDefault() => RunTest("""
		class Config
			Retries: int = 3
			Other: int

			constructor Create()
				super Create()
				Other = 1
			end
		end

		let c = Config.Create()
		print c.Retries.ToString()
		""",
		"""
		3

		""");

	[Test]
	public void ClassFieldDefaultCanBeAnExpression() => RunTest("""
		class Box
			Sum: int = 2 + 3

			constructor Create()
				super Create()
			end
		end

		let b = Box.Create()
		print b.Sum.ToString()
		""",
		"""
		5

		""");

	[Test]
	public void ClassFieldDefaultChangedByConstructor() => RunTest("""
		class Foo
			X: int = 3

			constructor Create()
				super Create()
				X = 9
			end
		end

		let f = Foo.Create()
		print f.X.ToString()
		""",
		"""
		9

		""");

	[Test]
	public void ClassFieldDefaultsThroughInheritance() => RunTest("""
		class Base
			A: int = 1

			constructor Create()
				super Create()
			end
		end

		class Derived extends Base
			B: int = 2

			constructor Create(x: int)
				super Create()
				B += x
			end

			constructor Make()
				Create(10)
				A += 1
			end
		end

		let d = Derived.Make()
		let b: Base = d
		print b.A.ToString()
		print d.B.ToString()
		""",
		"""
		2
		12

		""");

	[Test]
	public void ClassAbstractMethodIsImplementedBySubclass() => RunTest("""
		class abstract Shape
			constructor Create()
				super Create()
			end

			def abstract Area(): int

			def Describe(): string
				return "shape"
			end
		end

		class Square extends Shape
			Side: int = 3

			constructor Create()
				super Create()
			end

			def override Area(): int
				return Side * Side
			end
		end

		var s: Shape = Square.Create()
		print s.Area().ToString()
		print s.Describe()
		""",
		"""
		9
		shape

		""");

	[Test]
	public void ClassAbstractWithoutAbstractMembers() => RunTest("""
		class abstract Base
			constructor Create()
				super Create()
			end

			def Name(): string
				return "base"
			end
		end

		class Derived extends Base
			constructor Create()
				super Create()
			end
		end

		var b: Base = Derived.Create()
		print b.Name()
		""",
		"""
		base

		""");

	[Test]
	public void ClassAbstractIntermediateClass() => RunTest("""
		class abstract A
			constructor Create()
				super Create()
			end

			def abstract F(): int
		end

		class abstract B extends A
			constructor Create()
				super Create()
			end
		end

		class C extends B
			constructor Create()
				super Create()
			end

			def override F(): int
				return 4
			end
		end

		var a: A = C.Create()
		print a.F().ToString()
		""",
		"""
		4

		""");

	[Test]
	public void ClassAbstractClassScopeMethodThroughMeta() => RunTest("""
		class abstract Plugin
			constructor virtual Create()
				super Create()
			end

			def class abstract Id(): int
		end

		class Echo extends Plugin
			constructor override Create()
				super Create()
			end

			def class override Id(): int
				return 7
			end
		end

		def IdOf(cls: meta Plugin): int
			return cls.Id()
		end

		def Make(cls: meta Plugin): Plugin
			return cls.Create()
		end

		print IdOf(meta Echo).ToString()
		print Echo.Id().ToString()
		print Make(meta Echo).Id().ToString()
		""",
		"""
		7
		7
		7

		""");

	// An expression made of expressions, over a class's own fields by their bare names: each field gets its
	// `self`, which rebuilds the expressions around it, and they have to stay what they were.
	[Test]
	public void ClassNestedArithmeticOverBareFieldNames() => RunTest("""
		class Box
			W: int = 3
			H: int = 4

			constructor Create()
				super Create()
			end

			def Score(): int
				return W * 10 + H
			end

			def Spread(): int
				return (W + H) * (W - H) - -W
			end
		end

		var b = Box.Create()
		print b.Score().ToString()
		print b.Spread().ToString()
		""",
		"""
		34
		-4

		""");
}
