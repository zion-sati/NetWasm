using System.Collections.Immutable;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

// Records what one compilation wrote in the artifact manifest, including the
// native callback object when nwc wrote one, and publishes the resolved artifacts.
public sealed class NetWasmWriteCompileArtifactManifestTask : CompilerArtifactManifestTaskBase
{
    private readonly ICompilerArtifactManifestTaskRequestBuilder _requestBuilder;
    private readonly ICompilerArtifactManifestBuilder _manifestBuilder;
    private readonly ICompilerArtifactManifestWriter _manifestWriter;

    public NetWasmWriteCompileArtifactManifestTask()
        : this(
            CompilerTaskComposition.CreateArtifactManifestTaskRequestBuilder(),
            CompilerTaskComposition.CreateArtifactManifestBuilder(),
            CompilerTaskComposition.CreateArtifactManifestWriter())
    {
    }

    internal NetWasmWriteCompileArtifactManifestTask(
        ICompilerArtifactManifestTaskRequestBuilder requestBuilder,
        ICompilerArtifactManifestBuilder manifestBuilder,
        ICompilerArtifactManifestWriter manifestWriter)
    {
        _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
        _manifestBuilder = manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));
        _manifestWriter = manifestWriter ?? throw new ArgumentNullException(nameof(manifestWriter));
    }

    public string? NativeCallbackObjectPath { get; set; }

    public override bool Execute()
    {
        try
        {
            var callbackArtifacts = string.IsNullOrWhiteSpace(NativeCallbackObjectPath) ||
                !File.Exists(NativeCallbackObjectPath)
                ? ImmutableArray<CompilerArtifactManifestTaskArtifact>.Empty
                : [CreateNativeCallbackSupportArtifact(NativeCallbackObjectPath)];
            var result = _manifestBuilder.Build(_requestBuilder.Build(CreateManifestTaskInput(callbackArtifacts)));
            _manifestWriter.Write(new(ArtifactManifestPath, result.Manifest));
            SetResolvedArtifacts(result);
            return true;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }
}
