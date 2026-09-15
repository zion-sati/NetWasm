namespace NetWasm.Sdk.Pack.Restore;

public interface IRestoreGraphCanonicalizer
{
    byte[] Canonicalize(byte[] graph, IReadOnlyDictionary<string, string> profiles);
}
