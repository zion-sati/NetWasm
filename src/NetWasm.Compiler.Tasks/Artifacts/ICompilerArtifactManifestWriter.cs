namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface ICompilerArtifactManifestWriter
{
    void Write(CompilerArtifactManifestWriteRequest request);
}
