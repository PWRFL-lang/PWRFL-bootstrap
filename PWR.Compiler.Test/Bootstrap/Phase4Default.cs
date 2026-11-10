namespace PWR.Compiler.Test.Bootstrap;

internal class Phase4Default : Phase4
{
	// Default Parameters

	[Test]
	public void DefaultParams() => RunTest("""
		def addOffset(x: int, y: int = 10): int
			return x + y
		end

		print addOffset(5).ToString()     ; use default value
		print addOffset(5, 20).ToString() ; use explicit value
		""",
		"""
		15
		25

		""");

	[Test]
	public void NamedArguments() => RunTest("""
		def sub(x: int, y: int): int
			return x - y
		end

		print sub(x: 10, y: 3).ToString()
		print sub(y: 3, x: 10).ToString()
		print sub(10, y: 3).ToString()
		""",
		"""
		7
		7
		7

		""");

	[Test]
	public void NonConstantDefault() => RunError("""
		def bar(): int
			return 5
		end

		def foo(x: int = bar()): int
			return x
		end

		print foo().ToString()
		""",
		"must be a constant");

	[Test]
	public void NonDefaultAfterDefault() => RunError("""
		def foo(x: int = 5, y: int): int
			return x + y
		end

		print foo(1, 2).ToString()
		""",
		"cannot follow a defaulted parameter");

	[Test]
	public void PositionalAfterNamed() => RunError("""
		def foo(x: int, y: int): int
			return x + y
		end

		print foo(x: 5, 20).ToString()
		""",
		"positional argument cannot follow a named argument");

	[Test]
	public void ConstantFoldedDefault() => RunTest("""
		def foo(x: int = 3 + 2): int
			return x
		end

		print foo().ToString()
		""",
		"""
		5

		""");
}
