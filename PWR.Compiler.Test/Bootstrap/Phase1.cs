using System.Runtime.InteropServices;
using System.Text;

namespace PWR.Compiler.Test.Bootstrap;

/*
 * Compiler bootstrapping phase 1: demonstrate that basic language concepts work.
 * Build executable code with LLVM JIT, executing in memory, hijacking STDOUT to
 * our own StringBuilder.
 */
public class Phase1
{
	private PwrCompiler _compiler = null!;
	private PutsDelegate _del;

	[OneTimeSetUp]
	public void Setup()
	{
		_del = new PutsDelegate(TestPuts);
		var fnPtr = Marshal.GetFunctionPointerForDelegate(_del);
		_compiler = new PwrCompiler(fnPtr);
	}

	private static readonly StringBuilder _capturedOutput = new();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate int PutsDelegate(IntPtr fmt);

	static int TestPuts(IntPtr fmt)
	{
		string s = Marshal.PtrToStringAnsi(fmt)!;
		_capturedOutput.AppendLine(s);
		return s.Length;
	}

	private void RunTest(string code, string expected)
	{
		_capturedOutput.Clear();
		var compiled = _compiler.CompileToMemory(code);
		Assert.That(compiled, Is.Not.Null);
		compiled();
		var result = _capturedOutput.ToString();
		Assert.That(result, Is.EqualTo(expected));
	}

	// working: 3/19/2026
	[Test]
	public void HelloWorld() => RunTest("print \"Hello, World!\"", "Hello, World!\r\n");

	// working: 3/20/2026
	[Test]
	public void Fib() => RunTest(
		"""
		def fib(i: int): int
			if i == 1
				return 1
			elif i == 2
				return 1
			else
				return fib(i - 1) + fib(i - 2)
			end
		end

		var result = fib(5)
		if result == 5
			print "5"
		else
			print "Error"
		end
		""",
		"5\r\n");

	// working: 3/21/2026
	[Test]
	public void FibMatch() => RunTest(
		"""
		def fib(i: int): int
			return match i { 1, 2: 1, else fib(i - 1) + fib(i - 2) }
		end

		var result = fib(5)
		print if result == 5 then "5" else "Error"
		""",
		"5\r\n");

	// working: 4/1/2026
	[Test]
	public void IntToStr() => RunTest(
		"""
		def intMagnitude(i: int): int
			if i >= 10000
				if i >= 1000000
					return if i >= 100000000 then 9 + ord(i >= 1000000000) else 7 + ord(i >= 10000000)
				else
					return 5 + ord(i >= 100000)
				end
			else
				return if i >= 100 then 3 + ord(i >= 1000) else 1 + ord(i >= 10)
			end
		end

		def doIntToStr(value: int, mag: int, result: char span)
			for i in range(mag - 1, 0, -1)
				let digit = value % 10
				result[i] = '0' + digit
				value //= 10
			end
		end

		def intToStrPos(i: int): string
			let mag = intMagnitude(i)
			var result = new char[mag]
			doIntToStr(i, mag, result)
			return new string(result)
		end

		def intToStrNeg(i: int): string
			let mag = intMagnitude(i)
			var result = new char[mag + 1]
			result[0] = '-'
			doIntToStr(i, mag, result[1..])
			return new string(result)
		end
		
		def IntToStr(i: int): string
			return if i < 0 then intToStrNeg(-i) else intToStrPos(i)
		end

		print IntToStr(5)
		print IntToStr(-5)
		print IntToStr(45)
		print IntToStr(1346)
		print IntToStr(-456789)
		print IntToStr(123456789)
		print IntToStr(-987654321)
		""",
		"""
		5
		-5
		45
		1346
		-456789
		123456789
		-987654321

		""");

	// working: 4/1/2026
	[Test]
	public void While() => RunTest(
		"""
		def intMagnitude(i: int): int
			if i >= 10000
				if i >= 1000000
					return if i >= 100000000 then 9 + ord(i >= 1000000000) else 7 + ord(i >= 10000000)
				else
					return 5 + ord(i >= 100000)
				end
			else
				return if i >= 100 then 3 + ord(i >= 1000) else 1 + ord(i >= 10)
			end
		end
		
		def doIntToStr(value: int, mag: int, result: char span)
			for i in range(mag - 1, 0, -1)
				let digit = value % 10
				result[i] = '0' + digit
				value //= 10
			end
		end
		
		def intToStrPos(i: int): string
			let mag = intMagnitude(i)
			var result = new char[mag]
			doIntToStr(i, mag, result)
			return new string(result)
		end
		
		def intToStrNeg(i: int): string
			let mag = intMagnitude(i)
			var result = new char[mag + 1]
			result[0] = '-'
			doIntToStr(i, mag, result[1..])
			return new string(result)
		end
		
		def IntToStr(i: int): string
			return if i < 0 then intToStrNeg(-i) else intToStrPos(i)
		end

		var total = 0
		var i = 0
		while i < 10
			total += i
			i += 1
		end
		print IntToStr(total)
		""",
		"45\r\n"
	);
}
