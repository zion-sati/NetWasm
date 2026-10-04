using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmReadRawBindingManifestTask : Microsoft.Build.Utilities.Task
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly IRawBindingManifestReader _reader;

    public NetWasmReadRawBindingManifestTask() : this(new RawBindingManifestReader()) { }

    internal NetWasmReadRawBindingManifestTask(IRawBindingManifestReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    [Required] public string ManifestPath { get; set; } = string.Empty;
    [Required] public string Target { get; set; } = string.Empty;
    [Output] public ITaskItem[] RequiredImports { get; private set; } = [];
    [Output] public ITaskItem[] RequiredImportModules { get; private set; } = [];

    public override bool Execute()
    {
        RequiredImports = [];
        RequiredImportModules = [];
        try
        {
            var manifest = _reader.Read(ManifestPath, Target);
            RequiredImports = [.. manifest.RequiredImports.Select(CreateFunctionItem)];
            RequiredImportModules =
            [
                .. manifest.RequiredImports.Select(item => item.Interface)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .Select(item => new TaskItem(item)),
            ];
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK063: {exception.Message}");
            return false;
        }
    }

    private static TaskItem CreateFunctionItem(WitInterfaceFunction function)
    {
        var item = new TaskItem($"{function.Interface}/{function.Name}");
        item.SetMetadata("Interface", function.Interface);
        item.SetMetadata("Name", function.Name);
        item.SetMetadata("Parameters", JsonSerializer.Serialize(function.Parameters, MetadataJsonOptions));
        item.SetMetadata("Results", JsonSerializer.Serialize(function.Results, MetadataJsonOptions));
        return item;
    }
}
