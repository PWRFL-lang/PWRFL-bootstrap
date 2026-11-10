namespace PWR.Compiler.Test.Bootstrap;

internal class Phase4Metaclasses : Phase4
{

	[Test]
	public void ClassVirtualConstructorViaMeta() => RunTest("""
		class Shape
			constructor virtual Create()
				super Create()
			end

			def virtual Area(): int
				return 0
			end
		end

		class Square extends Shape
			Side: int = 5

			constructor override Create()
				super Create()
			end

			def override Area(): int
				return Side * Side
			end
		end

		def MakeShape(cls: meta Shape): Shape
			return cls.Create()
		end

		var s = MakeShape(meta Square)
		print s.Area().ToString()
		""",
		"""
		25

		""");

	[Test]
	public void MetaclassValueAssignableToItself() => RunTest("""
		class Shape
			constructor virtual Create()
				super Create()
			end

			def virtual Area(): int
				return 1
			end
		end

		def Make(cls: meta Shape): Shape
			return cls.Create()
		end

		let s = Make(meta Shape)
		print s.Area().ToString()
		""",
		"""
		1

		""");

	[Test]
	public void MetaclassInheritance() => RunTest("""
		class Shape
			constructor virtual Create()
				super Create()
			end

			def virtual Area(): int
				return 0
			end
		end

		class Square extends Shape
			Side: int = 5

			constructor override Create()
				super Create()
			end

			def override Area(): int
				return Side * Side
			end
		end

		var m: meta Shape = meta Square
		let a = m.Create()
		m = meta Shape
		let b = m.Create()
		print a.Area().ToString()
		print b.Area().ToString()
		""",
		"""
		25
		0

		""");

	[Test]
	public void MetaclassDisallowsNonVirtualConstructors() => RunError("""
		class Shape
			constructor Create()
				super Create()
			end

			def virtual Area(): int
				return 1
			end
		end

		class Square extends Shape
			Side: int = 5

			def override Area(): int
				return Side + 7
			end
		end

		def Make(cls: meta Shape): Shape
			return cls.Create()
		end

		var s = Make(meta Square)
		print s.Area().ToString()
		""",
		"does not contain a member named 'Create'");

	[Test]
	public void MetaclassOfStructIsError() => RunError("""
		struct Point
			X: int
		end

		var m = meta Point
		""",
		"'meta' can only be applied to a class");

	[Test]
	public void MetaclassOfPrimitiveIsError() => RunError("""
		var m = meta int
		""",
		"'meta' can only be applied to a class");

	[Test]
	public void MetaclassTypeOfStructIsError() => RunError("""
		struct Point
			X: int
		end

		def Make(cls: meta Point)
		end
		""",
		"'meta' can only be applied to a class");

	[Test]
	public void MetaclassDowncastIsError() => RunError("""
		class Shape
			constructor Create()
				super Create()
			end
		end

		class Square extends Shape
			constructor Create()
				super Create()
			end
		end

		var m: meta Square = meta Shape
		""",
		"cannot be assigned");

	[Test]
	public void MetaclassUnrelatedClassesIsError() => RunError("""
		class A
			constructor virtual Create()
				super Create()
			end
		end

		class B
			constructor Create()
				super Create()
			end
		end

		def Make(cls: meta A): A
			return cls.Create()
		end

		var a = Make(meta B)
		""",
		"cannot be passed as a parameter");

	[Test]
	public void MetaclassIsNotAnInstance() => RunError("""
		class Shape
			constructor Create()
				super Create()
			end
		end

		def Touch(s: Shape)
		end

		Touch(meta Shape)
		""",
		"cannot be passed as a parameter");

	[Test]
	public void ClassMetaValueHasNoInstanceMembers() => RunError("""
		class Shape
			constructor Create()
				super Create()
			end

			def Area(): int
				return 0
			end
		end

		def Make(cls: meta Shape): int
			return cls.Area()
		end
		""",
		"does not contain a member named 'Area'");

	private const string AnyMetaclasses = """
		class Animal
			constructor virtual Create()
				super Create()
			end

			const class Home: meta = meta Animal
		end

		class Dog extends Animal
			constructor override Create()
				super Create()
			end

			const class override Home = meta Dog
		end

		class Zoo
			Kind: meta

			constructor Create(kind: meta)
				super Create()
				Kind = kind
			end
		end

		def Same(a: meta, b: meta): bool
			return a == b
		end

		def Pick(wantDog: bool): meta
			if wantDog
				return meta Dog
			end
			return meta Animal
		end

		""";

	[Test]
	public void BareMetaHoldsAnyMetaclass() => RunTest(AnyMetaclasses + """
		var any: meta = meta Dog
		var zoo = Zoo.Create(meta Animal)
		if Same(any, Pick(true))
			print "dog is dog"
		end
		if Same(any, Pick(false))
			print "dog is animal"
		else
			print "dog is not animal"
		end
		if Same(zoo.Kind, meta Animal)
			print "zoo has animal"
		end
		""",
		"""
		dog is dog
		dog is not animal
		zoo has animal

		""");

	[Test]
	public void BareMetaclassWithClassVirtualConstant() => RunTest(AnyMetaclasses + """
		var d = Dog.Create()
		if Same(d.Home, meta Dog)
			print "dog home"
		end
		if Same(Animal.Home, meta Animal)
			print "animal home"
		end
		""",
		"""
		dog home
		animal home

		""");

	[Test]
	public void BareMetaIsNotAssignableToASpecificMeta() => RunError(AnyMetaclasses + """
		var any: meta = meta Dog
		var dog: meta Dog = any
		""",
		"cannot be assigned to a variable of type 'meta Dog'");

	[Test]
	public void ClassBareMetaHasNoMembers() => RunError(AnyMetaclasses + """
		def Make(m: meta): string
			return m.Create().ToString()
		end
		""",
		"Type 'meta' does not contain a member named 'Create'");

	[Test]
	public void BareMetaIsATypeNotAValue() => RunParseError("""
		var x = meta
		""",
		"'meta' as a value needs a class");

}
