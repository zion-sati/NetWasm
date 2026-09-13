using System.Collections.Immutable;
using Microsoft.Build.Framework;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal sealed record CompilerArtifactManifestTaskInput(
    string ManifestPath,
    string ProjectDirectory,
    string Profile,
    string Target,
    string FeatureSet,
    string SdkVersion,
    string CompilerVersion,
    string RuntimeAbiVersion,
    string RuntimeVersion,
    string InputAssemblyPath,
    ImmutableArray<ITaskItem> References,
    ImmutableArray<ITaskItem> Sources,
    string? RuntimeAbiManifestPath,
    ImmutableArray<ITaskItem> Artifacts,
    string? GeneratedSourceRoot = null);
