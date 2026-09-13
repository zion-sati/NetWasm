namespace NetWasm.Compiler.Metadata;

public interface IManagedAssemblyImageReader
{
    byte[] Read(string path);
}
