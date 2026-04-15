using PWR.Compiler.Helpers;
using PWR.Compiler.Semantics;
using PWR.Compiler.TypeSystem;
using System;
using System.Diagnostics;
using System.IO;

namespace PWR.Compiler.Metadata;

internal enum SigElement : byte
{
	// simple types
	Void = 0,
	Bool = 1,
	Char = 2,
	I8 = 3,
	U8 = 4,
	I16 = 5,
	U16 = 6,
	I32 = 7,
	U32 = 8,
	I64 = 9,
	U64 = 10,
	F16 = 11,
	F32 = 12,
	F64 = 13,
	Ptr = 14,
	String = 15,
	Reserved1 = 16,

	// modified type tags, followed by the type being referenced
	Ref = 20,
	SimpleArray = 21, // dynamic array, rank 1
	Array = 22,       // more complicated arrays
	Span = 23,
	Sequence = 24,
	Box = 25,
	Meta = 26,        // metaclass
	Reserved2 = 27,
	
	// composite types, followed by a type token
	Module = 40,
	Struct = 41,
	Class = 42,
	Reserved3 = 43,

	FunctionPointer = 50, //followed by function signature
	Reserved4 = 51,
}

internal enum SigType : byte
{
	Field,
	Property,
	Method,
}

internal static class Signatures
{
	public static byte[] WriteField(IType type, MetadataContext context)
	{
		var sw = new SpanWriter(stackalloc byte[255]);
		try {
			sw.Write((byte)SigType.Field);
			WriteType(type, ref sw, context);
			return sw.ToArray();
		} catch (IndexOutOfRangeException) {
			var buffer = new byte[2048];
			try {
				sw = new SpanWriter(buffer);
				sw.Write((byte)SigType.Field);
				WriteType(type, ref sw, context);
				return sw.ToArray();
			} catch (IndexOutOfRangeException) {
				throw new Exception("Signature too long");
			}
		}
	}

	public static byte[] WriteProperty(IType type, MetadataContext context)
	{
		var sw = new SpanWriter(stackalloc byte[255]);
		try {
			sw.Write((byte)SigType.Property);
			WriteType(type, ref sw, context);
			return sw.ToArray();
		} catch (IndexOutOfRangeException) {
			var buffer = new byte[2048];
			try {
				sw = new SpanWriter(buffer);
				sw.Write((byte)SigType.Property);
				WriteType(type, ref sw, context);
				return sw.ToArray();
			} catch (IndexOutOfRangeException) {
				throw new Exception("Signature too long");
			}
		}
	}

	public static byte[] WriteMethod(IFunction func, MetadataContext context)
	{
		var sw = new SpanWriter(stackalloc byte[255]);
		try {
			sw.Write((byte)SigType.Method);
			sw.Write7BitEncodedInt(func.Args.Length);
			foreach (var arg in func.Args) {
				WriteType(arg.Semantic!.Type, ref sw, context);
			}
			WriteType(func.ReturnType?.Semantic!.Type ?? Types.Void, ref sw, context);
			return sw.ToArray();
		} catch (IndexOutOfRangeException) {
			var buffer = new byte[2048];
			try {
				sw = new SpanWriter(buffer);
				sw.Write((byte)SigType.Method);
				sw.Write7BitEncodedInt(func.Args.Length);
				foreach (var arg in func.Args) {
					WriteType(arg.Semantic!.Type, ref sw, context);
				}
				WriteType(func.ReturnType?.Semantic!.Type ?? Types.Void, ref sw, context);
				return sw.ToArray();
			} catch (IndexOutOfRangeException) {
				throw new Exception("Signature too long");
			}
		}
	}

	private static void WriteType(IType type, ref SpanWriter writer, MetadataContext context)
	{
		switch (type) {
			case PrimitiveType pt:
				WritePrimitiveType(pt, ref writer);
				break;
			case StringType:
				writer.Write((byte)SigElement.String);
				break;
			case PointerType:
				writer.Write((byte)SigElement.Ptr);
				break;
			case ICollectionType ct:
				WriteCollectionType(ct, ref writer, context);
				break;
			case RefType rt:
				writer.Write((byte)SigElement.Ref);
				WriteType(rt.BaseType, ref writer, context);
				break;
			case ICompositeType ct2:
				writer.Write((byte)SigElement.Struct);
				writer.Write(context.LookupType(ct2).AsInt);
				break;
			default:
				throw new NotImplementedException();
		}
	}

	private static void WritePrimitiveType(PrimitiveType pt, ref SpanWriter writer)
	{
		var tag = pt.Name switch {
			"void" => SigElement.Void,
			"bool" => SigElement.Bool,
			"int" => SigElement.I32,
			"char" => SigElement.Char,
			"ptr" => SigElement.Ptr,
			_ => throw new NotImplementedException()
		};
		writer.Write((byte)tag);
	}

	private static void WriteCollectionType(ICollectionType ct, ref SpanWriter writer, MetadataContext context)
	{
		var tag = ct switch {
			ArrayType => SigElement.Array,
			SpanType => SigElement.Span,
			SequenceType => SigElement.Sequence,
			_ => throw new NotImplementedException(),
		};
		writer.Write((byte)tag);
		WriteType(ct.BaseType, ref writer, context);
	}

	internal static IType ReadField(byte[] sig, MetadataContext context)
	{
		if (sig[0] != (byte)SigType.Field) {
			throw new Exception("Invalid field signature");
		}
		var sr = new SpanReader(sig.AsSpan(1));
		var result = ReadType(ref sr, context);
		Debug.Assert(sr.Position == sr.Length);
		return result;
	}

	internal static IType[] ReadFunc(byte[] sig, MetadataContext context)
	{
		if (sig[0] != (byte)SigType.Method) {
			throw new Exception("Invalid field signature");
		}
		var sr = new SpanReader(sig.AsSpan(1));
		var count = sr.Read7BitEncodedInt();
		var result = new IType[count + 1];
		for (int i = 0; i <= count; ++i) { //yes, >=, because we're reading all the params + return type
			result[i] = ReadType(ref sr, context);
		}
		Debug.Assert(sr.Position == sr.Length);
		return result;
	}

	private static IType ReadType(ref SpanReader sr, MetadataContext context)
	{
		var element = (SigElement)sr.ReadByte();
		return element switch {
			SigElement.Void => Types.Void,
			SigElement.Bool => Types.Bool,
			SigElement.I32 => Types.Int32,
			SigElement.Char => Types.Char,
			SigElement.Ptr => Types.Ptr,
			SigElement.String => Types.String,
			SigElement.Ref => RefType.Create(ReadType(ref sr, context)),
			SigElement.Array => ArrayType.Create(ReadType(ref sr, context)),
			SigElement.Span => SpanType.Create(ReadType(ref sr, context)),
			SigElement.Sequence => new SequenceType(ReadType(ref sr, context)),
			_ => throw new NotImplementedException()
		};
	}
}
