namespace PWR.Compiler.Test.Bootstrap;

internal class Phase4Arrays : Phase4
{
	[Test]
	public void ArrayAlloc() => RunTest("""
		var a = new int[5]
		a[0] = 10
		a[1] = 20
		a[2] = 30
		print a[0].ToString()
		print a.Length.ToString()
		""",
		"""
		10
		5

		""");

	[Test]
	public void ArrayResize() => RunTest("""
		var a = new int[3]
		a[0] = 1
		a[1] = 2
		a[2] = 3
		a.Resize(6)
		a[3] = 4
		a[4] = 5
		a[5] = 6
		print a[3].ToString()
		print a.Length.ToString()
		""",
		"""
		4
		6

		""");

	[Test]
	public void ArrayResizePreservesValues() => RunTest("""
		var a = new int[3]
		a[0] = 42
		a.Resize(10)
		print a[0].ToString()
		""",
		"""
		42

		""");


	[Test]
	public void ArrayResizeShrink() => RunTest("""
		var a = new int[10]
		a[0] = 99
		a.Resize(3)
		print a[0].ToString()
		print a.Length.ToString()
		""",
		"""
		99
		3

		""");
}
