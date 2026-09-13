using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal interface ICompilerArtifactManifestTaskRequestBuilder
{
    CompilerArtifactManifestBuildRequest Build(CompilerArtifactManifestTaskInput input);
}
