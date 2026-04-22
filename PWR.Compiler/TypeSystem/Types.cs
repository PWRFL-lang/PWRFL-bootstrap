using System;
using System.Collections.Generic;
using System.Linq;

using LLVMSharp;

namespace PWR.Compiler.TypeSystem;

public static class Types
{
	internal static void Populate(LLVMContext context)
	{
		var handle = context.Handle;
		Void = new PrimitiveType(handle.VoidType, "void");
		Bool = new PrimitiveType(handle.Int1Type, "bool");
		Int32 = new PrimitiveType(handle.Int32Type, "int");
		Int64 = new PrimitiveType(handle.Int64Type, "long");
		Byte = new PrimitiveType(handle.Int8Type, "byte");
		Char = new PrimitiveType(handle.Int8Type, "char");
		String = new StringType();
		Ptr = new PointerType();
		Nil = new PointerType();
		BuildCompatibleTypes();
	}

	public static IType Void { get; private set; } = null!;
	public static IType Bool { get; private set; } = null!;
	public static IType Int32 { get; private set; } = null!;
	public static IType Int64 { get; private set; } = null!;
	public static IType String { get; private set; } = null!;
	public static IType Byte { get; private set; } = null!;
	public static IType Char { get; private set; } = null!;
	public static IType Ptr { get; private set; } = null!;
	public static IType Nil { get; private set; } = null!;

	private static ILookup<IType, IType> _compatibleTypes = null!;
	
	private static void BuildCompatibleTypes() => _compatibleTypes = new List<KeyValuePair<IType, IType>>() {
		KeyValuePair.Create(Int32, Char),
		KeyValuePair.Create(Char, Int32),
		KeyValuePair.Create(Int32, Byte),
	}.ToLookup(kvp => kvp.Key, kvp => kvp.Value);

	public static bool IsCompatible(IType l, IType r)
	{
		if (_compatibleTypes.Contains(l) && _compatibleTypes[l].Contains(r)) { 
			return true;
		}
		if (l is NilableType nl) {
			if (r == Nil) {
				return true;
			}
			if (r is not NilableType) {
				return IsCompatible(nl.BaseType, r);
			}
		}
		if (l is RefType lr && r == lr.BaseType) {
			return true;
		}
		return false;
	}
}