namespace NetWasm.Toolchain.Prerequisites;

public interface IHostPathCanonicalizer
{
    string Canonicalize(string path);
}
