using System.Linq;

using PWR.Compiler.Ast;
using PWR.Compiler.Semantics;

namespace PWR.Compiler.TypeSystem.Internal;

public class InternalStruct(StructDeclaration decl) : ICompositeType
{
	public string Name => Decl.Name.ToString();

	public StructDeclaration Decl { get; } = decl;

	public ISemantic[] Fields
	{
		get {
			field ??= [.. Decl.Body.OfType<FieldDeclaration>().Select(d => d.Semantic!)];
			return field;
		}
	}

	ISemantic? IType.GetMember(string name) => Decl.Body.FirstOrDefault(d => d.Semantic?.Name == name)?.Semantic;

	public override string ToString() => Name;
}
