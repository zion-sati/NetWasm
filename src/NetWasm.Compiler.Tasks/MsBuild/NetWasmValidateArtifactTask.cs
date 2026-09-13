using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmValidateArtifactTask : CompilerArtifactManifestTaskBase
{
    private readonly ICompilerArtifactManifestTaskRequestBuilder _requestBuilder;
    private readonly ICompilerArtifactManifestValidator _validator;

    public NetWasmValidateArtifactTask()
        : this(
            CompilerTaskComposition.CreateArtifactManifestTaskRequestBuilder(),
            CompilerTaskComposition.CreateArtifactManifestValidator())
    {
    }

    internal NetWasmValidateArtifactTask(
        ICompilerArtifactManifestTaskRequestBuilder requestBuilder,
        ICompilerArtifactManifestValidator validator)
    {
        _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public override bool Execute()
    {
        try
        {
            var request = _requestBuilder.Build(CreateManifestTaskInput());
            SetResolvedArtifacts(_validator.Validate(new(request, ArtifactManifestPath)));
            return true;
        }
        catch (Exception)
        {
            Log.LogError("NWSDK003: NetWasm artifact manifest is missing, invalid, or stale; rebuild the application.");
            return false;
        }
    }
}
