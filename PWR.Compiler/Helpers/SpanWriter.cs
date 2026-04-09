using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace PWR.Compiler.Helpers;

// adapted from code found at https://github.com/StefH/Span.ReaderWriter/blob/main/src/Span.ReaderWriter/SpanWriter.cs
public ref struct SpanWriter
{
	private const int BufferLength = 16;

	public readonly Span<byte> Span;
	private readonly Encoding _encoding;
	private readonly Encoder _encoder;

	private readonly char[] _singleChar;
	private readonly byte[] _buffer; // temp space for writing primitives to.

	public int Length;
	public int Position;

	public SpanWriter(Span<byte> span) : this(span, Encoding.UTF8)
	{ }

	public SpanWriter(Span<byte> span, Encoding encoding)
	{
		Span = span;
		_encoding = encoding;
		_encoder = encoding.GetEncoder();
		Length = span.Length;
		Position = 0;

		_singleChar = new char[1];
		_buffer = new byte[BufferLength];
	}

	public readonly byte[] ToArray() => Span[..Position].ToArray();

	public readonly Span<byte> AsSpan() => Span[..Position];

	public int Write(byte value, int? position = null)
	{
		Span[position ?? Position] = value;
		return UpdatePosition(1, position);
	}

	public int Write(string value, int? position = null)
	{
		int numberOfBytes = _encoding.GetByteCount(value);
		int bytesWritten = Write7BitEncodedInt(numberOfBytes, position);

		var bytes = _encoding.GetBytes(value);
		return bytesWritten + Write(bytes, position);
	}

	public int Write(ReadOnlySpan<byte> byteSpan, int? position = null) => Write(byteSpan, byteSpan.Length, position);

	public int Write(ReadOnlySpan<byte> byteSpan, int length, int? position = null)
	{
		byteSpan.CopyTo(Span[(position ?? Position)..]);

		return UpdatePosition(length, position);
	}

	public int Write(char value, int? position = null)
	{
		_singleChar[0] = value;

		var numBytes = _encoder.GetBytes(_singleChar, 0, 1, _buffer, 0, true);
		return Write(_buffer, numBytes, position);
	}

	public int Write(char[] chars, int? position = null)
	{
		byte[] bytes = _encoding.GetBytes(chars, 0, chars.Length);
		return Write(bytes, position);
	}

	public int Write(decimal value, int? position = null) => Write(DecimalToBytes(value), position);

	public int Write(DateTime value, int? position = null) => Write(value.ToBinary(), position);

	public int Write(Guid value, int? position = null) => Write(value.ToByteArray(), position);

	public int Write<T>(T value, int? position = null) where T : unmanaged
	{
		MemoryMarshal.Write(Span[(position ?? Position)..], in value);

		var length = Unsafe.SizeOf<T>();
		return UpdatePosition(length, position);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private readonly byte[] DecimalToBytes(decimal number)
	{
		var decimalBits = decimal.GetBits(number);

		var lo = decimalBits[0];
		var mid = decimalBits[1];
		var hi = decimalBits[2];
		var flags = decimalBits[3];

		_buffer[0] = (byte)lo;
		_buffer[1] = (byte)(lo >> 8);
		_buffer[2] = (byte)(lo >> 16);
		_buffer[3] = (byte)(lo >> 24);

		_buffer[4] = (byte)mid;
		_buffer[5] = (byte)(mid >> 8);
		_buffer[6] = (byte)(mid >> 16);
		_buffer[7] = (byte)(mid >> 24);

		_buffer[8] = (byte)hi;
		_buffer[9] = (byte)(hi >> 8);
		_buffer[10] = (byte)(hi >> 16);
		_buffer[11] = (byte)(hi >> 24);

		_buffer[12] = (byte)flags;
		_buffer[13] = (byte)(flags >> 8);
		_buffer[14] = (byte)(flags >> 16);
		_buffer[15] = (byte)(flags >> 24);

		return _buffer;
	}

	// Based on https://github.com/dotnet/runtime/blob/1d9e50cb4735df46d3de0cee5791e97295eaf588/src/libraries/System.Private.CoreLib/src/System/IO/BinaryWriter.cs#L466
	public int Write7BitEncodedInt(int value, int? position = null)
	{
		int bytesWritten = 0;
		uint uValue = (uint)value;

		// Write out an int 7 bits at a time. The high bit of the byte,
		// when on, tells reader to continue reading more bytes.
		//
		// Using the constants 0x7F and ~0x7F below offers smaller
		// codegen than using the constant 0x80.

		while (uValue > 0x7Fu) {
			bytesWritten += Write((byte)(uValue | ~0x7Fu), position);
			uValue >>= 7;
		}

		return bytesWritten + Write((byte)uValue, position);
	}

	// Based on https://github.com/dotnet/runtime/blob/1d9e50cb4735df46d3de0cee5791e97295eaf588/src/libraries/System.Private.CoreLib/src/System/IO/BinaryWriter.cs#L485
	public int Write7BitEncodedInt64(long value, int? position = null)
	{
		int bytesWritten = 0;
		ulong uValue = (ulong)value;

		// Write out an int 7 bits at a time. The high bit of the byte,
		// when on, tells reader to continue reading more bytes.
		//
		// Using the constants 0x7F and ~0x7F below offers smaller
		// codegen than using the constant 0x80.

		while (uValue > 0x7Fu) {
			bytesWritten += Write((byte)((uint)uValue | ~0x7Fu), position);
			uValue >>= 7;
		}

		return bytesWritten + Write((byte)uValue, position);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int UpdatePosition(int length, int? position)
	{
		// Only update the Position during a "normal" Write, else keep it the same.
		if (position is null) {
			Position += length;
		}

		return length;
	}
}
