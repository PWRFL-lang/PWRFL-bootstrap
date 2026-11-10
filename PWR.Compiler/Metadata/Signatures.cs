using PWR.Compiler.Ast;
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
	Nilable = 27,     // a type that may also be nil
	FixedArray = 28,  // inline array, followed by the element type, then the length (7-bit encoded)
	AnyMeta = 29,     // bare `meta`: the metaclass of any class. Nothing follows it.
	Reserved2 = 30,
	
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
			case MetaclassType { IsAny: true }:
				writer.Write((byte)SigElement.AnyMeta);
				break;
			case MetaclassType mt:
				writer.Write((byte)SigElement.Meta);
				WriteType(mt.Class, ref writer, context);
				break;
			case NilableType nt:
				writer.Write((byte)SigElement.Nilable);
				WriteType(nt.BaseType, ref writer, context);
				break;
			case InlineArrayType ia:
				writer.Write((byte)SigElement.FixedArray);
				WriteType(ia.ElementType, ref writer, context);
				writer.Write7BitEncodedInt(FixedArrayLength(ia));
				break;
			case ICollectionType ct:
				WriteCollectionType(ct, ref writer, context);
				break;
			case RefType rt:
				writer.Write((byte)SigElement.Ref);
				WriteType(rt.BaseType, ref writer, context);
				break;
			case ICompositeType ct2:
				writer.Write((byte)(ct2 is IClassType ? SigElement.Class : SigElement.Struct));
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
			"byte" => SigElement.I8,
			"int" => SigElement.I32,
			"long" => SigElement.I64,
			"char" => SigElement.Char,
			"ptr" => SigElement.Ptr,
			_ => throw new NotImplementedException()
		};
		writer.Write((byte)tag);
	}

	private static int FixedArrayLength(InlineArrayType ia)
		=> ia.Size is IntegerLiteralExpression il
			? il.Value
			: throw new CompileError(ia.Size, "Fixed-size array must have a constant integer size");

	private static void WriteCollectionType(ICollectionType ct, ref SpanWriter writer, MetadataContext context)
	{
		var tag = ct switch {
			ArrayType => SigElement.Array,
			SpanType => SigElement.Span,
			SequenceType => SigElement.Sequence,
			_ => throw new NotImplementedException(),
		};
		writer.Write((byte)tag);
		WriteType(ct.ElementType, ref writer, context);
	}

	internal static byte[] WriteConstant(Expression value, MetadataContext? context = null)
	{
		using var ms = new MemoryStream();
		using var bw = new BinaryWriter(ms);
		switch (value) {
			case IntegerLiteralExpression il:
				bw.Write((byte)SigElement.I32);
				bw.Write(il.Value);
				break;
			case CharLiteralExpression cl:
				bw.Write((byte)SigElement.Char);
				bw.Write(cl.Value);
				break;
			case StringLiteralExpression sl:
				bw.Write((byte)SigElement.String);
				bw.Write(sl.Value);
				break;
			case BooleanLiteralExpression bl:
				bw.Write((byte)SigElement.Bool);
				bw.Write(bl.Value);
				break;
			case MetaclassExpression me when context != null:
				bw.Write((byte)SigElement.Meta);
				bw.Write(context.LookupType((IClassType)me.Type.Semantic!.Type).AsInt);
				break;
			case NilLiteralExpression:
				bw.Write((byte)SigElement.Ptr);
				break;
			default:
				throw new NotImplementedException($"Unsupported constant type: {value.GetType().Name}");
		}
		return ms.ToArray();
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
			SigElement.I8 => Types.Byte,
			SigElement.I32 => Types.Int32,
			SigElement.I64 => Types.Int64,
			SigElement.Char => Types.Char,
			SigElement.Ptr => Types.Ptr,
			SigElement.String => Types.String,
			SigElement.Ref => RefType.Create(ReadType(ref sr, context)),
			SigElement.Nilable => NilableType.Create(ReadType(ref sr, context)),
			SigElement.AnyMeta => MetaclassType.Any,
			SigElement.Meta => ReadType(ref sr, context) is IClassType cls
				? MetaclassType.Create(cls)
				: throw new Exception("Invalid metadata: a metaclass type must refer to a class."),
			SigElement.FixedArray => ReadFixedArray(ref sr, context),
			SigElement.Struct or SigElement.Class => context.ResolveType(sr.ReadInt32()),
			SigElement.Array => ArrayType.Create(ReadType(ref sr, context)),
			SigElement.Span => SpanType.Create(ReadType(ref sr, context)),
			SigElement.Sequence => new SequenceType(ReadType(ref sr, context)),
			_ => throw new NotImplementedException()
		};
	}

	private static InlineArrayType ReadFixedArray(ref SpanReader sr, MetadataContext context)
	{
		var element = ReadType(ref sr, context);
		var length = sr.Read7BitEncodedInt();
		return new InlineArrayType(element, new IntegerLiteralExpression(default, length) { Semantic = new TypeRef(Types.Int32) });
	}

	private static MetaclassExpression ReadMetaclassConstant(ref SpanReader sr, MetadataContext? context)
	{
		var token = sr.ReadInt32();
		if (context?.ResolveType(token) is not IClassType cls) {
			throw new Exception("Invalid metadata: a metaclass constant must refer to a class.");
		}
		var expression = new MetaclassExpression(new SimpleTypeReference(default, cls.Name) { Semantic = new TypeRef(cls) });
		expression.Semantic = new MetaclassLiteral(expression, MetaclassType.Create(cls));
		return expression;
	}

	internal static Expression ReadConstant(Span<byte> data, MetadataContext? context = null)
	{
		var sr = new SpanReader(data);
		var typ = (SigElement)sr.ReadByte();
		return typ switch
		{
			SigElement.I32 => new IntegerLiteralExpression(default, sr.ReadInt32()) { Semantic = new TypeRef(Types.Int32) },
			SigElement.Char => new CharLiteralExpression(default, sr.ReadChar()) { Semantic = new TypeRef(Types.Char) },
			SigElement.String => new StringLiteralExpression(default, sr.ReadString()) { Semantic = new TypeRef(Types.String) },
			SigElement.Bool => new BooleanLiteralExpression(default, sr.ReadBoolean()) { Semantic = new TypeRef(Types.Bool) },
			SigElement.Ptr => new NilLiteralExpression(default) { Semantic = new TypeRef(Types.Nil) },
			SigElement.Meta => ReadMetaclassConstant(ref sr, context),
			_ => throw new NotImplementedException($"Unsupported constant type: {typ}")
		};
	}
}
