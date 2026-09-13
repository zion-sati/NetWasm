namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface ICompilerArtifactManifestBuilder
{
    CompilerArtifactManifestBuildResult Build(CompilerArtifactManifestBuildRequest request);
}
