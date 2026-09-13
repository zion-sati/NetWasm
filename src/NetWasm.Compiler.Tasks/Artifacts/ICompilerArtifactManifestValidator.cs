namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface ICompilerArtifactManifestValidator
{
    CompilerArtifactManifestBuildResult Validate(CompilerArtifactManifestValidationRequest request);
}
