namespace NetWasm.Sdk.Pack.Archives;

public interface ISymbolPackageWriter
{
    SymbolPackageOutput? Write(CanonicalPackage package);
}
