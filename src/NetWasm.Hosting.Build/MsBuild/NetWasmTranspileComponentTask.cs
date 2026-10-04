using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmTranspileComponentTask : Microsoft.Build.Utilities.Task
{
    private readonly IComponentTranspiler _transpiler;
    private readonly ICanonicalComponentAdapterWriter _adapters;
    private readonly IWitWorkerComponentAdapterWriter _witWorkerAdapters;
    private readonly IBuildArtifactStore _artifacts;

    public NetWasmTranspileComponentTask()
        : this(
            new ComponentTranspiler(),
            new CanonicalComponentAdapterWriter(),
            new WitWorkerComponentAdapterWriter(WitWorkerContractComposition.CreateReader(), new WitWorkerValueLayoutPlanner()),
            new BuildArtifactStore())
    {
    }

    internal NetWasmTranspileComponentTask(
        IComponentTranspiler transpiler,
        ICanonicalComponentAdapterWriter adapters,
        IWitWorkerComponentAdapterWriter witWorkerAdapters,
        IBuildArtifactStore artifacts)
    {
        _transpiler = transpiler ?? throw new ArgumentNullException(nameof(transpiler));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _witWorkerAdapters = witWorkerAdapters ??
            throw new ArgumentNullException(nameof(witWorkerAdapters));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    [Required] public string NodePath { get; set; } = string.Empty;
    [Required] public string JcoPath { get; set; } = string.Empty;
    [Required] public string JcoVersion { get; set; } = string.Empty;
    [Required] public string ComponentPath { get; set; } = string.Empty;
    [Required] public string OutputDirectory { get; set; } = string.Empty;
    [Required] public string BaseName { get; set; } = string.Empty;
    [Required] public string AdapterPath { get; set; } = string.Empty;
    public string ExecutionContract { get; set; } = string.Empty;
    public string WorkerContractPath { get; set; } = string.Empty;
    public string RelativeDirectory { get; set; } = string.Empty;

    [Output] public ITaskItem[] Artifacts { get; private set; } = [];

    public override bool Execute()
    {
        Artifacts = [];
        try
        {
            var result = _transpiler.Transpile(new(
                Path.GetFullPath(NodePath),
                Path.GetFullPath(JcoPath),
                Path.GetFullPath(ComponentPath),
                Path.GetFullPath(OutputDirectory),
                BaseName));
            _artifacts.Write(
                Path.GetFullPath(AdapterPath),
                string.IsNullOrWhiteSpace(WorkerContractPath)
                    ? _adapters.Write(new(ExecutionContract, JcoVersion))
                    : _witWorkerAdapters.Write(new(
                        _artifacts.Read(Path.GetFullPath(WorkerContractPath)),
                        JcoVersion,
                        [.. result.RootExports.Select(value =>
                            new WitWorkerRootExport(value.Name, value.Kind))])));
            Artifacts =
            [
                CreateArtifact(AdapterPath, "component-adapter", "text/javascript"),
                CreateArtifact(result.JavaScriptPath, "component-javascript", "text/javascript"),
                .. result.CoreModulePaths.Select(path =>
                    CreateArtifact(path, "component-core-module", "application/wasm")),
            ];
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK045: {exception.Message}");
            return false;
        }
    }

    private TaskItem CreateArtifact(string path, string role, string mediaType)
    {
        var fullPath = Path.GetFullPath(path);
        var relativePath = string.IsNullOrWhiteSpace(RelativeDirectory)
            ? Path.GetFileName(fullPath)
            : string.Concat(
                RelativeDirectory.Trim('/'),
                "/",
                Path.GetFileName(fullPath));
        var item = new TaskItem(fullPath);
        item.SetMetadata("RelativePath", relativePath);
        item.SetMetadata("Role", role);
        item.SetMetadata("MediaType", mediaType);
        return item;
    }
}
