namespace PWR.Compiler.TypeSystem;

public interface IModule: IType
{
	IType? ExtendsType { get; }
}
