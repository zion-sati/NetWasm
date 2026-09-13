namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface IArtifactFileDigestCalculator
{
    string Calculate(string path);
}
