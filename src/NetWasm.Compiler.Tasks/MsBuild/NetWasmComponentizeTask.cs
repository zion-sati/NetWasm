using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmComponentizeTask : Microsoft.Build.Utilities.Task
{
    private readonly IComponentBuildSessionFactory _sessions;
    private readonly IComponentManifestInputsReader _manifestInputs;
    private readonly IComponentWitWorldSelector _witWorlds;
    private readonly IComponentManifestWriter _manifests;
    private readonly IManagedEntryPointReader _entryPoints;

    public NetWasmComponentizeTask()
        : this(
            CompilerTaskComposition.CreateComponentBuildSessionFactory(),
            CompilerTaskComposition.CreateComponentManifestInputsReader(),
            CompilerTaskComposition.CreateComponentWitWorldSelector(),
            CompilerTaskComposition.CreateComponentManifestWriter(),
            new ManagedEntryPointReader())
    {
    }

    internal NetWasmComponentizeTask(
        IComponentBuildSessionFactory sessions,
        IComponentManifestInputsReader manifestInputs,
        IComponentWitWorldSelector witWorlds,
        IComponentManifestWriter manifests,
        IManagedEntryPointReader entryPoints)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _manifestInputs = manifestInputs ??
            throw new ArgumentNullException(nameof(manifestInputs));
        _witWorlds = witWorlds ?? throw new ArgumentNullException(nameof(witWorlds));
        _manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
        _entryPoints = entryPoints ?? throw new ArgumentNullException(nameof(entryPoints));
    }

    [Required]
    public string InputAssemblyPath { get; set; } = string.Empty;

    [Required]
    public string CoreModulePath { get; set; } = string.Empty;

    [Required]
    public string RuntimeModulePath { get; set; } = string.Empty;

    [Required]
    public string WitPath { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;

    public ITaskItem[] WitWorldVariants { get; set; } = [];

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string ComponentManifestPath { get; set; } = string.Empty;

    [Required]
    public string InteropManifestPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsNodePath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsCommandPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsModulePath { get; set; } = string.Empty;

    [Required]
    public string BinaryenWasmOptPath { get; set; } = string.Empty;

    [Required]
    public string BinaryenWasmMergePath { get; set; } = string.Empty;

    [Required]
    public string Target { get; set; } = string.Empty;

    public string JcoVersion { get; set; } = string.Empty;

    public string Preview2ShimVersion { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] Components { get; private set; } = [];

    [Output]
    public string SelectedWitPath { get; private set; } = string.Empty;

    [Output]
    public string SelectedWorld { get; private set; } = string.Empty;

    public override bool Execute()
    {
        Components = [];
        SelectedWitPath = string.Empty;
        SelectedWorld = string.Empty;
        try
        {
            var componentTarget = Target switch
            {
                "wasm32" => ComponentTarget.Wasm32Wasi02,
                "wasm64" => ComponentTarget.Wasm64Wasi02,
                _ => throw new InvalidOperationException(
                    "The NetWasm component target must be wasm32 or wasm64."),
            };
            var inputs = _manifestInputs.Read(new(
                InteropManifestPath,
                Target,
                NullIfEmpty(JcoVersion),
                NullIfEmpty(Preview2ShimVersion)));
            var wit = _witWorlds.Select(new(
                WitPath,
                NullIfEmpty(World),
                [.. WitWorldVariants.Select(CreateWitWorldVariant)],
                inputs.WitImports));
            SelectedWitPath = wit.Path;
            SelectedWorld = wit.World ?? string.Empty;
            var entryPoint = _entryPoints.Read(InputAssemblyPath);
            using var session = _sessions.Create(
                CreateWasmToolsCommand(),
                CreateBinaryenConfiguration());
            var manifest = session.Build(new(
                CoreModulePath,
                RuntimeModulePath,
                wit.Path,
                wit.World,
                OutputPath,
                componentTarget,
                inputs,
                entryPoint.Abi));
            _manifests.Write(new(ComponentManifestPath, manifest));
            Components = [CreateComponentItem(manifest)];
            return true;
        }
        catch (CompilerException exception)
        {
            Log.LogError(exception.Diagnostic.ToString());
            return false;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK021: {exception.Message}");
            return false;
        }
    }

    private ExternalToolCommand CreateWasmToolsCommand() =>
        CompilerTaskComposition.CreateWasmToolsCommand(
            WasmToolsNodePath,
            WasmToolsCommandPath,
            WasmToolsModulePath);

    private BinaryenToolRunnerConfiguration CreateBinaryenConfiguration() =>
        CompilerTaskComposition.CreateBinaryenConfiguration(
            WasmToolsNodePath,
            BinaryenWasmOptPath,
            BinaryenWasmMergePath);

    private TaskItem CreateComponentItem(ComponentManifest manifest)
    {
        var item = new TaskItem(Path.GetFullPath(OutputPath));
        item.SetMetadata("Kind", "Component");
        item.SetMetadata("MediaType", "application/wasm");
        item.SetMetadata("WasmTarget", manifest.Target);
        item.SetMetadata("WasiVersion", manifest.WasiVersion);
        item.SetMetadata("World", manifest.World);
        return item;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ComponentWitWorldVariant CreateWitWorldVariant(ITaskItem item) =>
        new(
            item.ItemSpec,
            NullIfEmpty(item.GetMetadata("World")),
            item.GetMetadata("ActivationInterfacePrefix"));
}
