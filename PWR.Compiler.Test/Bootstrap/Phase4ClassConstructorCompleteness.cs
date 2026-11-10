namespace PWR.Compiler.Test.Bootstrap;

// Ensure that a constructor:
// * Calls a base or sibling constructor exactly once
// * Does not create sibling constructor-call cyles
// * Does not touch instance methods or state before the base/sibling constructor call
// * Ends with all fields definitely assigned
internal class Phase4ClassConstructorCompleteness : Phase4
{
	[Test]
	public void ClassDefiniteAssignmentOnBothBranches() => RunTest("""
		class Foo
			X: int

			constructor Create(cond: bool)
				super Create()
				if cond
					X = 1
				else
					X = 2
				end
			end
		end

		let f = Foo.Create(false)
		print f.X.ToString()
		""",
		"""
		2

		""");

	[Test]
	public void ClassDefinitelyAssignedElifChainWithElse() => RunTest("""
		class Foo
			X: int

			constructor Create(a: bool, b: bool)
				super Create()
				if a
					X = 1
				elif b
					X = 2
				else
					X = 3
				end
			end
		end

		let f = Foo.Create(false, true)
		print f.X.ToString()
		""",
		"""
		2

		""");

	[Test]
	public void ClassDefiniteAssignmentElifChainWithoutElse() => RunError("""
		class Foo
			X: int

			constructor Create(a: bool, b: bool)
				super Create()
				if a
					X = 1
				elif b
					X = 2
				end
			end
		end

		Foo.Create(true, false)
		""",
		"Field 'X' is not definitely assigned");

	// A loop body may run zero times, so assigning in it isn't enough.
	[Test]
	public void ClassDefiniteAssignmentInsideLoopIsNotEnough() => RunError("""
		class Foo
			X: int

			constructor Create(n: int)
				super Create()
				var i = 0
				while i < n
					X = i
					i += 1
				end
			end
		end

		Foo.Create(3)
		""",
		"Field 'X' is not definitely assigned");

	[Test]
	public void ClassDefiniteAssignmentReportsEveryMissingField() => RunError("""
		class Foo
			X: int
			Y: int
			Z: int

			constructor Create()
				super Create()
				Y = 1
			end
		end

		Foo.Create()
		""",
		"Fields 'X', 'Z' are not definitely assigned");

	[Test]
	public void ClassDefaultFieldMayBeAssignedConditionally() => RunTest("""
		class Foo
			X: int = 7

			constructor Create(cond: bool)
				super Create()
				if cond
					X = 5
				end
			end
		end

		let a = Foo.Create(true)
		let b = Foo.Create(false)
		print a.X.ToString()
		print b.X.ToString()
		""",
		"""
		5
		7

		""");

	[Test]
	public void ClassSiblingConstructorSatisfiesAssignment() => RunTest("""
		class Foo
			X: int

			constructor Create(x: int)
				super Create()
				X = x
			end

			constructor Seven()
				Create(7)
			end
		end

		let f = Foo.Seven()
		print f.X.ToString()
		""",
		"""
		7

		""");

	[Test]
	public void ClassDefiniteAssignmentConstantTrueCondition() => RunTest("""
		class Foo
			X: int

			constructor Create()
				super Create()
				if true
					X = 4
				end
			end
		end

		let f = Foo.Create()
		print f.X.ToString()
		""",
		"""
		4

		""");

	[Test]
	public void ClassDefiniteAssignmentConstantFalseCondition() => RunError("""
		class Foo
			X: int

			constructor Create()
				super Create()
				if false
					X = 4
				end
			end
		end

		Foo.Create()
		""",
		"Field 'X' is not definitely assigned");

	// Each arm of an `if` may make its own base or sibling call, as long as every path makes one.
	[Test]
	public void ClassConstructorCallOnEachBranch() => RunTest("""
		class Foo
			X: int

			constructor Create(cond: bool)
				if cond
					super Create()
					X = 1
				else
					super Create()
					X = 2
				end
			end
		end

		let f = Foo.Create(false)
		print f.X.ToString()
		""",
		"""
		2

		""");

	[Test]
	public void ClassSiblingDelegationOnEachBranch() => RunTest("""
		class Foo
			X: int

			constructor Create(x: int)
				super Create()
				X = x
			end

			constructor Make(cond: bool)
				if cond
					Create(1)
				else
					Create(2)
				end
			end
		end

		let f = Foo.Make(false)
		print f.X.ToString()
		""",
		"""
		2

		""");

	[Test]
	public void ClassConstructorCallOnOnlyOneBranch() => RunError("""
		class Foo
			constructor Create(cond: bool)
				if cond
					super Create()
				end
			end
		end

		let f = Foo.Create(true)
		""",
		"must call a base or sibling constructor");

	[Test]
	public void ClassConstructorCallTwiceOnOnePath() => RunError("""
		class Foo
			constructor Create()
				super Create()
				super Create()
			end
		end

		let f = Foo.Create()
		""",
		"Only one call to a base or sibling constructor");

	// The call inside the `if` and the unconditional one after it both run when `cond` is true.
	[Test]
	public void ClassConstructorCallOnOnePathAndThenUnconditionally() => RunError("""
		class Foo
			constructor Create(cond: bool)
				if cond
					super Create()
				end
				super Create()
			end
		end

		let f = Foo.Create(true)
		""",
		"Only one call to a base or sibling constructor");

	[Test]
	public void ClassFieldWriteBeforeSuperOnOneBranch() => RunError("""
		class Foo
			X: int

			constructor Create(cond: bool)
				if cond
					super Create()
					X = 1
				else
					X = 2
					super Create()
				end
			end
		end

		let f = Foo.Create(true)
		""",
		"before the base constructor has been called");

	[Test]
	public void ClassSuperMethodCallBeforeSuperConstructor() => RunError("""
		class Base
			constructor Create()
				super Create()
			end

			def Hello()
			end
		end

		class Derived extends Base
			constructor Create()
				super Hello()
				super Create()
			end
		end
		""",
		"before the base constructor has been called");

	// Only `self` is off limits before the base constructor runs; other objects are fine.
	[Test]
	public void ClassOtherObjectsMayBeUsedBeforeSuper() => RunTest("""
		class Foo
			X: int

			constructor Create(x: int)
				super Create()
				X = x
			end

			constructor Copy(other: Foo)
				let v = other.X
				super Create()
				X = v
			end
		end

		let a = Foo.Create(5)
		let b = Foo.Copy(a)
		print b.X.ToString()
		""",
		"""
		5

		""");

	[Test]
	public void ClassConstructorConditionalDelegationCycle() => RunError("""
		class Foo
			constructor A(cond: bool)
				if cond
					B()
				else
					super Create()
				end
			end

			constructor B()
				A(true)
			end
		end

		let f = Foo.A(false)
		""",
		"constructor cycle");

	// The second iteration of the loop would call the base constructor again.
	[Test]
	public void ClassConstructorCallInsideLoop() => RunError("""
		class Foo
			constructor Create(n: int)
				var i = 0
				while i < n
					super Create()
					i += 1
				end
			end
		end
		""",
		"Only one call to a base or sibling constructor");

	[Test]
	public void ClassSelfPassedAsArgumentBeforeSuper() => RunError("""
		class Foo
			constructor Create()
				Touch(self)
				super Create()
			end
		end

		def Touch(f: Foo)
		end
		""",
		"'self' cannot be used before the base constructor has been called");

	[Test]
	public void ClassSelfNestedInArgumentBeforeSuper() => RunError("""
		class Foo
			constructor Create()
				Touch(Same(self))
				super Create()
			end
		end

		def Same(f: Foo): Foo
			return f
		end

		def Touch(f: Foo)
		end
		""",
		"'self' cannot be used before the base constructor has been called");

	[Test]
	public void ClassSelfStoredBeforeSuper() => RunError("""
		class Foo
			constructor Create()
				var s = self
				super Create()
			end
		end
		""",
		"'self' cannot be used before the base constructor has been called");

	[Test]
	public void ClassSelfPassedToSuperConstructor() => RunError("""
		class Base
			constructor Create(other: Base)
				super Create()
			end
		end

		class Derived extends Base
			constructor Create()
				super Create(self)
			end
		end
		""",
		"'self' cannot be used before the base constructor has been called");

	[Test]
	public void ClassSelfBeforeSuperOnOneBranch() => RunError("""
		class Foo
			constructor Create(cond: bool)
				if cond
					Touch(self)
					super Create()
				else
					super Create()
				end
			end
		end

		def Touch(f: Foo)
		end
		""",
		"'self' cannot be used before the base constructor has been called");

	[Test]
	public void ClassSelfMayBeUsedAfterSuper() => RunTest("""
		class Foo
			X: int

			constructor Create()
				super Create()
				X = 3
				Show(self)
			end
		end

		def Show(f: Foo)
			Console.PrintLn(f.X.ToString())
		end

		Foo.Create()
		""",
		"""
		3

		""");

	[Test]
	public void ClassExplicitSelfSiblingDelegation() => RunTest("""
		class Foo
			X: int

			constructor Create(x: int)
				super Create()
				X = x
			end

			constructor Seven()
				self.Create(7)
			end
		end

		let f = Foo.Seven()
		print f.X.ToString()
		""",
		"""
		7

		""");

	[Test]
	public void ClassConstructorMissingSuperOrDelegate() => RunError("""
		class Foo
			constructor Create()
			end
		end
		""",
		"must call a base or sibling constructor");

	[Test]
	public void ClassFieldWriteBeforeSuper() => RunError("""
		class Foo
			X: int

			constructor Create(x: int)
				X = x
				super Create()
			end
		end
		""",
		"before the base constructor has been called");

	[Test]
	public void ClassMethodCallBeforeSuper() => RunError("""
		class Foo
			constructor Create()
				Bar()
				super Create()
			end

			def Bar()
			end
		end
		""",
		"before the base constructor has been called");

	[Test]
	public void ClassDefiniteAssignmentViolation() => RunError("""
		class Foo
			X: int

			constructor Create(cond: bool)
				super Create()
				if cond
					X = 5
				end
			end
		end
		""",
		"definitely assigned");

	[Test]
	public void ClassConstructorDelegationCycle() => RunError("""
		class Foo
			constructor A()
				B()
			end

			constructor B()
				A()
			end
		end
		""",
		"constructor cycle");

	// A virtual constructor called through a metaclass must be valid for the class and not violate
	// constructor completeness rules. A class that adds fields with defaults and doesn't have an
	// overridden virtual constructor gets an implicit override: it calls `super` with its
	// arguments, and then the field defaults are assigned.
	// A class that adds fields *without* defaults and does not override base-class virtual
	// consstructors raises a compile-time error
	private const string Shapes = """
		class Shape
			Sides: int

			constructor virtual Create(sides: int = 4)
				super Create()
				Sides = sides
			end

			def virtual Area(): int
				return Sides
			end
		end

		def Make(cls: meta Shape): Shape
			return cls.Create(3)
		end

		""";

	[Test]
	public void ClassWithDefaultedFieldsGetsAnImplicitConstructorOverride() => RunTest(Shapes + """
		class Square extends Shape
			Side: int = 5

			def override Area(): int
				return Side * 10 + Sides
			end
		end

		let s = Make(meta Square)
		print s.Area().ToString()
		""",
		"""
		53

		""");

	// The implicit override has the inherited constructor's parameters, defaults included, so it can be called
	// by the class's own name as well.
	[Test]
	public void ImplicitConstructorOverrideTakesTheInheritedParameters() => RunTest(Shapes + """
		class Square extends Shape
			Side: int = 5

			def override Area(): int
				return Side * 10 + Sides
			end
		end

		let a = Square.Create(7)
		let b = Square.Create()
		print a.Area().ToString()
		print b.Area().ToString()
		""",
		"""
		57
		54

		""");

	[Test]
	public void ClassWithNoFieldsOfItsOwnInheritsTheVirtualConstructor() => RunTest(Shapes + """
		class Triangle extends Shape
			def override Area(): int
				return Sides * 100
			end
		end

		let t = Make(meta Triangle)
		print t.Area().ToString()
		""",
		"""
		300

		""");

	[Test]
	public void ImplicitConstructorOverridesChain() => RunTest(Shapes + """
		class Square extends Shape
			Side: int = 5

			def override Area(): int
				return Side
			end
		end

		class Cube extends Square
			Depth: int = 6

			def override Area(): int
				return Side * 100 + Depth * 10 + Sides
			end
		end

		let c = Make(meta Cube)
		print c.Area().ToString()
		""",
		"""
		563

		""");

	[Test]
	public void ImplicitConstructorOverrideWithNoNewFields() => RunTest(Shapes + """
		class Square extends Shape
			Side: int = 5

			def override Area(): int
				return Side * 10 + Sides
			end
		end

		class Tile extends Square
		end

		let t = Make(meta Tile)
		print t.Area().ToString()
		""",
		"""
		53

		""");

	[Test]
	public void AbstractClassWithDefaultedFields() => RunTest(Shapes + """
		class abstract Solid extends Shape
			Density: int = 9
		end

		class Block extends Solid
			def override Area(): int
				return Density * 10 + Sides
			end
		end

		let b = Make(meta Block)
		print b.Area().ToString()
		""",
		"""
		93

		""");

	[Test]
	public void ClassWithAnotherConstructorStillGetsTheInheritedOneOverridden() => RunTest(Shapes + """
		class Square extends Shape
			Side: int = 5

			constructor Square(side: int)
				super Create(2)
				Side = side
			end

			def override Area(): int
				return Side * 10 + Sides
			end
		end

		let s = Make(meta Square)
		let t = Square.Square(8)
		print s.Area().ToString()
		print t.Area().ToString()
		""",
		"""
		53
		82

		""");

	[Test]
	public void ClassWithAnUndefaultedFieldMustOverrideVirtualConstructors() => RunError(Shapes + """
		class Square extends Shape
			Side: int

			def override Area(): int
				return Side
			end
		end
		""",
		"Class 'Square' must override the virtual constructor 'Shape.Create': its field 'Side' has no default value");

	[Test]
	public void ClassWithMultipleUndefaultedFields() => RunError(Shapes + """
		class Box extends Shape
			Width: int
			Height: int
			Depth: int = 1
		end
		""",
		"its fields 'Width', 'Height' have no default value");

	[Test]
	public void ClassWithAnUndefaultedFieldCanOverrideVirtualConstructors() => RunTest(Shapes + """
		class Square extends Shape
			Side: int

			constructor override Create(sides: int = 4)
				super Create(sides)
				Side = 6
			end

			def override Area(): int
				return Side * 10 + Sides
			end
		end

		let s = Make(meta Square)
		print s.Area().ToString()
		""",
		"""
		63

		""");
}
