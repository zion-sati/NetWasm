namespace NetWasm.Runtime.Pack.Materialization;

internal interface IArtifactDigestCalculator
{
    string Calculate(string path);
}
