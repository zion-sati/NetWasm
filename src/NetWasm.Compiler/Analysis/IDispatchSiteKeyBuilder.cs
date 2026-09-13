namespace NetWasm.Compiler.Analysis;

internal interface IDispatchSiteKeyBuilder
{
    string Build(string caller, int offset);
}
