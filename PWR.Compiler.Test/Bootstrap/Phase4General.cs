namespace PWR.Compiler.Test.Bootstrap;

internal class Phase4General : Phase4
{
	[Test]
	public void TypedVariableDeclarations() => RunTest("""
		class Box
			constructor Create()
				super Create()
			end

			def Size(): int
				return 3
			end
		end

		let n: int = 5
		let b: Box = Box.Create()
		print n.ToString()
		print b.Size().ToString()
		""",
		"""
		5
		3

		""");

	[Test]
	public void ProgramModuleInitializersRunBeforeMain() => RunTest("""
		module First
			Started: int = Announce()

			def Announce(): int
				print "first"
				return 1
			end
		end

		module Second
			Started: int = Announce()

			def Announce(): int
				print "second"
				return 2
			end
		end

		print "main"
		print First.Started.ToString()
		print Second.Started.ToString()
		""",
		"""
		first
		second
		main
		1
		2

		""");
}
