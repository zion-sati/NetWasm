namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface ICompilerArtifactWriter
{
    void Write(CompilerArtifactWriteRequest request);
}
