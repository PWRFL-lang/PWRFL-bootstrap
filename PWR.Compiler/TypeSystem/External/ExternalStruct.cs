using PWR.Compiler.Metadata;

namespace PWR.Compiler.TypeSystem.External;

internal class ExternalStruct(string? ns, string name, IType? parent, MetadataContext context, int rowIdx)
	: ExternalType(ns, name, parent, context, rowIdx)
{
	protected override Token IdToken => new(MetadataContext.TYPE_DEF_ID, _rowIdx);
}
