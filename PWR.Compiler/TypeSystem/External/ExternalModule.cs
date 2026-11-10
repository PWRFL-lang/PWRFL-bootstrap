using PWR.Compiler.Metadata;

namespace PWR.Compiler.TypeSystem.External;

internal class ExternalModule(string? ns, string name, IType? parent, MetadataContext context, int rowIdx)
	: ExternalType(ns, name, parent, context, rowIdx), IModule
{
	private IType? _extendsType;
	private bool _extendsTypeRead;

	public IType? ExtendsType {
		get {
			if (!_extendsTypeRead) {
				_extendsType = ReadAssociatedType();
				_extendsTypeRead = true;
			}
			return _extendsType;
		}
	}

	protected override Token IdToken => new(MetadataContext.TYPE_DEF_ID, _rowIdx);
}
