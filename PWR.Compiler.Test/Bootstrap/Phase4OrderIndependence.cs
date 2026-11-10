namespace PWR.Compiler.Test.Bootstrap;

internal class Phase4OrderIndependence : Phase4
{
	[Test]
	public void FunctionTakesClassDeclaredLater() => RunTest("""
		def Show(f: Foo)
			Console.PrintLn(f.X.ToString())
		end

		class Foo
			X: int

			constructor Create(x: int)
				super Create()
				X = x
			end
		end

		var f = Foo.Create(4)
		Show(f)
		""",
		"""
		4

		""");

	[Test]
	public void FunctionReturnsClassDeclaredLater() => RunTest("""
		def Make(x: int): Foo
			return Foo.Create(x)
		end

		class Foo
			X: int

			constructor Create(x: int)
				super Create()
				X = x
			end
		end

		var f = Make(6)
		print f.X.ToString()
		""",
		"""
		6

		""");

	[Test]
	public void FunctionTakesStructDeclaredLater() => RunTest("""
		def Sum(p: Point): int
			return p.X + p.Y
		end

		struct Point
			X: int
			Y: int
		end

		var p = Point(1, 2)
		print Sum(p).ToString()
		""",
		"""
		3

		""");

	[Test]
	public void FunctionTakesTypeDeclaredInLaterFile() => RunTestFiles(
		[
			"""
			def Show(f: Foo)
				Console.PrintLn(f.X.ToString())
			end
			""",
			"""
			class Foo
				X: int

				constructor Create(x: int)
					super Create()
					X = x
				end
			end

			var f = Foo.Create(8)
			Show(f)
			"""
		],
		"""
		8

		""");
}
