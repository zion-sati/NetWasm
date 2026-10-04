using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmReadHostInteropManifestTask : Microsoft.Build.Utilities.Task
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly IHostInteropManifestReader _manifests;

    public NetWasmReadHostInteropManifestTask()
        : this(CompilerTaskComposition.CreateHostInteropManifestReader())
    {
    }

    internal NetWasmReadHostInteropManifestTask(IHostInteropManifestReader manifests)
    {
        _manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
    }

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    [Required]
    public string Target { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] Imports { get; private set; } = [];

    [Output]
    public ITaskItem[] Exports { get; private set; } = [];

    public override bool Execute()
    {
        Imports = [];
        Exports = [];
        try
        {
            var manifest = _manifests.Read(ManifestPath, Target);
            Imports =
            [
                .. manifest.Imports
                    .Where(import => import.Module != RuntimeAbi.HostModule)
                    .Select(CreateImport),
            ];
            Exports = [.. manifest.Exports.Select(CreateExport)];
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK060: {exception.Message}");
            return false;
        }
    }

    private static TaskItem CreateImport(HostInteropImport import)
    {
        var item = CreateFunction(import.Name, import.Parameters, import.Result, import.AsyncReturn);
        item.SetMetadata("Module", import.Module);
        return item;
    }

    private static TaskItem CreateExport(HostInteropExport export) =>
        CreateFunction(export.Name, export.Parameters, export.Result, export.AsyncReturn);

    private static TaskItem CreateFunction(
        string name,
        IReadOnlyCollection<string> parameters,
        string result,
        string? asyncReturn)
    {
        var item = new TaskItem(name);
        item.SetMetadata("Name", name);
        item.SetMetadata("Parameters", JsonSerializer.Serialize(parameters, MetadataJsonOptions));
        item.SetMetadata("Result", result);
        item.SetMetadata("AsyncReturn", asyncReturn ?? string.Empty);
        return item;
    }
}
