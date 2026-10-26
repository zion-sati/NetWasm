using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmWritePageHostEntryTask : Microsoft.Build.Utilities.Task
{
    private readonly IPageEntryWriter _writer;

    public NetWasmWritePageHostEntryTask() : this(new PageEntryWriter()) { }

    internal NetWasmWritePageHostEntryTask(IPageEntryWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string HostingJavaScriptRoot { get; set; } = string.Empty;
    [Required] public string Preview2ShimRoot { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    public ITaskItem[] Imports { get; set; } = [];
    public ITaskItem[] ImportModules { get; set; } = [];
    public ITaskItem[] Modules { get; set; } = [];
    public ITaskItem[] Arguments { get; set; } = [];
    public ITaskItem[] EnvironmentVariables { get; set; } = [];
    public ITaskItem[] Clocks { get; set; } = [];
    [Required] public string Network { get; set; } = string.Empty;
    public bool Randomness { get; set; }
    public ITaskItem[] RequiredImportModules { get; set; } = [];
    public bool ValidateDeploymentManifest { get; set; } = true;

    public override bool Execute()
    {
        try
        {
            // A page may supply a required module to open() instead of bundling
            // it, but a bundled module that no import uses is a project error.
            var required = Imports.Select(item => JavaScriptExportItems.RequiredMetadata(item, "Module"))
                .Concat(ImportModules.Select(item => item.ItemSpec))
                .ToHashSet(StringComparer.Ordinal);
            var modules = Modules
                .Where(item => item.GetMetadata("NetWasmGeneratedWorkerClient") != "true"
                    || required.Contains(JavaScriptExportItems.RequiredMetadata(item, "Module")))
                .Select(item => new BrowserJavaScriptModule(
                    JavaScriptExportItems.RequiredMetadata(item, "Module"),
                    Path.GetFullPath(item.ItemSpec)))
                .ToImmutableArray();
            var unused = modules.FirstOrDefault(module => !required.Contains(module.Module));
            if (unused is not null)
            {
                throw new ArgumentException($"Page JavaScript module '{unused.Module}' is unused.");
            }
            _writer.WriteHost(new(
                Path.GetFullPath(HostingJavaScriptRoot),
                Path.GetFullPath(Preview2ShimRoot),
                Path.GetFullPath(OutputPath),
                modules,
                [.. Arguments.Select(item => item.ItemSpec)],
                [.. EnvironmentVariables.Select(item => new WorkerEnvironmentVariable(
                    item.ItemSpec,
                    item.GetMetadata("Value")))],
                [.. Clocks.Select(item => item.ItemSpec)],
                Network,
                Randomness,
                [.. RequiredImportModules.Select(item => item.ItemSpec)],
                ValidateDeploymentManifest));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK077: {exception.Message}");
            return false;
        }
    }
}

public sealed class NetWasmWritePageModuleTask : Microsoft.Build.Utilities.Task
{
    private readonly IPageEntryWriter _writer;

    public NetWasmWritePageModuleTask() : this(new PageEntryWriter()) { }

    internal NetWasmWritePageModuleTask(IPageEntryWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string PageFileName { get; set; } = string.Empty;
    [Required] public string BuildFingerprint { get; set; } = string.Empty;
    [Required] public string ManifestSha256 { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    [Required] public string DeclarationPath { get; set; } = string.Empty;
    [Required] public ITaskItem[] Exports { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            _writer.WriteModule(new(
                PageFileName,
                BuildFingerprint,
                ManifestSha256,
                Path.GetFullPath(OutputPath),
                Path.GetFullPath(DeclarationPath),
                JavaScriptExportItems.Read(Exports)));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK078: {exception.Message}");
            return false;
        }
    }
}

/// <summary>Reads the export items <c>NetWasmReadHostInteropManifestTask</c> emits.</summary>
internal static class JavaScriptExportItems
{
    public static ImmutableArray<WorkerJavaScriptExport> Read(IEnumerable<ITaskItem> items) =>
        [.. items.Select(item => new WorkerJavaScriptExport(
            RequiredMetadata(item, "Name"),
            ReadSignature(item.GetMetadata("Parameters")),
            RequiredMetadata(item, "Result"),
            string.IsNullOrWhiteSpace(item.GetMetadata("AsyncReturn")) ? null : item.GetMetadata("AsyncReturn"))
        {
            ParameterNames = string.IsNullOrWhiteSpace(item.GetMetadata("ParameterNames"))
                ? []
                : ReadSignature(item.GetMetadata("ParameterNames")),
            NonNullableParameters = string.IsNullOrWhiteSpace(item.GetMetadata("NonNullableParameters"))
                ? []
                : [.. JsonSerializer.Deserialize<bool[]>(item.GetMetadata("NonNullableParameters"))
                    ?? throw new ArgumentException("JavaScript export nullability must be a JSON array.")],
            NonNullableResult = item.GetMetadata("NonNullableResult") == "true",
        })];

    public static string RequiredMetadata(ITaskItem item, string name)
    {
        var value = item.GetMetadata(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"JavaScript item metadata '{name}' is required.")
            : value;
    }

    private static ImmutableArray<string> ReadSignature(string json)
    {
        var values = JsonSerializer.Deserialize<string[]>(json) ??
            throw new ArgumentException("JavaScript export signatures must be JSON arrays.");
        if (values.Any(value => value is null))
        {
            throw new ArgumentException("JavaScript export signatures cannot contain null.");
        }
        return [.. values];
    }
}
