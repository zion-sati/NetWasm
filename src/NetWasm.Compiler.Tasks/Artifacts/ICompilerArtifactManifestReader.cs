namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface ICompilerArtifactManifestReader
{
    CompilerArtifactManifest Read(string path);
}
