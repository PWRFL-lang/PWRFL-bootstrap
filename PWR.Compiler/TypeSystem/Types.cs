using System;
using System.Collections.Generic;
using System.Linq;

using LLVMSharp;
using LLVMSharp.Interop;

namespace PWR.Compiler.TypeSystem;

public static class Types
{
	internal static void Populate(LLVMContext context)
	{
		var handle = context.Handle;
		Void = new PrimitiveType(handle.VoidType, "void");
		Bool = new PrimitiveType(handle.Int1Type, "bool");
		Int32 = new PrimitiveType(handle.Int32Type, "int");
		Char = new PrimitiveType(handle.Int8Type, "char");
		Ptr = new PrimitiveType(LLVMTypeRef.CreatePointer(handle.VoidType, 0), "ptr");
		BuildCompatibleTypes();
	}

	public static IType Void { get; private set; } = null!;
	public static IType Bool { get; private set; } = null!;
	public static IType Int32 { get; private set; } = null!;
	public static IType String { get; } = new StringType();
	public static IType Char { get; private set; } = null!;
	public static IType Ptr { get; private set; } = null!;

	private static ILookup<IType, IType> _compatibleTypes = null!;
	
	private static void BuildCompatibleTypes() => _compatibleTypes = new List<KeyValuePair<IType, IType>>() {
		KeyValuePair.Create(Int32, Char),
		KeyValuePair.Create(Char, Int32),
	}.ToLookup(kvp => kvp.Key, kvp => kvp.Value);

	public static bool IsCompatible(IType l, IType r) => _compatibleTypes.Contains(l) && _compatibleTypes[l].Contains(r);
}