using PWR.Compiler.Metadata;

namespace PWR.Compiler.TypeSystem.External;

internal class ExternalModule(string? ns, string name, IType? parent, IType? extendsType, MetadataContext context, int rowIdx)
	: ExternalType(ns, name, parent, context, rowIdx), IModule
{
	public IType? ExtendsType { get; } = extendsType;

	protected override Token IdToken => new(MetadataContext.TYPE_DEF_ID, _rowIdx);
}
