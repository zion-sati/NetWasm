namespace NetWasm.Compiler.Analysis;

internal sealed class DispatchSiteKeyBuilder : IDispatchSiteKeyBuilder
{
    public string Build(string caller, int offset) => $"{caller}@{offset:x8}";
}
