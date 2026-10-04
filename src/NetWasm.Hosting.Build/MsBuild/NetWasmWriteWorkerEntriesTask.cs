using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmWriteWorkerHostEntryTask : Microsoft.Build.Utilities.Task
{
    private readonly IWorkerEntryWriter _writer;

    public NetWasmWriteWorkerHostEntryTask() : this(new WorkerEntryWriter()) { }

    internal NetWasmWriteWorkerHostEntryTask(IWorkerEntryWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string HostingJavaScriptRoot { get; set; } = string.Empty;
    [Required] public string Preview2ShimRoot { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    public ITaskItem[] Imports { get; set; } = [];
    public ITaskItem[] Modules { get; set; } = [];
    public ITaskItem[] Arguments { get; set; } = [];
    public ITaskItem[] EnvironmentVariables { get; set; } = [];
    public ITaskItem[] Clocks { get; set; } = [];
    [Required] public string Network { get; set; } = string.Empty;
    public bool Randomness { get; set; }

    public override bool Execute()
    {
        try
        {
            var requiredModules = Imports.Select(item => RequiredMetadata(item, "Module"))
                .ToHashSet(StringComparer.Ordinal);
            var modules = Modules.Where(item =>
                    item.GetMetadata("NetWasmGeneratedWorkerClient") != "true"
                    || requiredModules.Contains(RequiredMetadata(item, "Module")))
                .ToDictionary(
                item => RequiredMetadata(item, "Module"),
                item => Path.GetFullPath(item.ItemSpec),
                StringComparer.Ordinal);
            var imports = Imports.GroupBy(
                    item => RequiredMetadata(item, "Module"),
                    StringComparer.Ordinal)
                .Select(group => new WorkerJavaScriptImport(
                    group.Key,
                    modules.Remove(group.Key, out var path)
                        ? path
                        : throw new ArgumentException(
                            $"Worker JavaScript module '{group.Key}' has no source binding."),
                    [.. group.Select(item => RequiredMetadata(item, "Name"))]))
                .ToImmutableArray();
            if (modules.Count != 0)
            {
                throw new ArgumentException(
                    $"Worker JavaScript module '{modules.Keys.Order(StringComparer.Ordinal).First()}' is unused.");
            }
            _writer.WriteHost(new(
                Path.GetFullPath(HostingJavaScriptRoot),
                Path.GetFullPath(Preview2ShimRoot),
                Path.GetFullPath(OutputPath),
                imports,
                [.. Arguments.Select(item => item.ItemSpec)],
                [.. EnvironmentVariables.Select(item => new WorkerEnvironmentVariable(
                    item.ItemSpec,
                    item.GetMetadata("Value")))],
                [.. Clocks.Select(item => item.ItemSpec)],
                Network,
                Randomness));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK061: {exception.Message}");
            return false;
        }
    }

    private static string RequiredMetadata(ITaskItem item, string name)
    {
        var value = item.GetMetadata(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Worker item metadata '{name}' is required.")
            : value;
    }
}

public sealed class NetWasmWriteWitWorkerHostEntryTask : Microsoft.Build.Utilities.Task
{
    private readonly IWitWorkerHostEntryWriter _writer;

    public NetWasmWriteWitWorkerHostEntryTask() : this(new WitWorkerHostEntryWriter()) { }

    internal NetWasmWriteWitWorkerHostEntryTask(IWitWorkerHostEntryWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string HostingJavaScriptRoot { get; set; } = string.Empty;
    [Required] public string Preview2ShimRoot { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    public ITaskItem[] Arguments { get; set; } = [];
    public ITaskItem[] ApplicationImports { get; set; } = [];
    public ITaskItem[] EnvironmentVariables { get; set; } = [];
    public ITaskItem[] Clocks { get; set; } = [];
    [Required] public string Network { get; set; } = string.Empty;
    public bool Randomness { get; set; }

    public override bool Execute()
    {
        try
        {
            _writer.Write(new(
                Path.GetFullPath(HostingJavaScriptRoot),
                Path.GetFullPath(Preview2ShimRoot),
                Path.GetFullPath(OutputPath),
                [.. Arguments.Select(item => item.ItemSpec)],
                [.. EnvironmentVariables.Select(item => new WorkerEnvironmentVariable(
                    item.ItemSpec,
                    item.GetMetadata("Value")))],
                [.. Clocks.Select(item => item.ItemSpec)],
                Network,
                Randomness,
                [.. ApplicationImports.Select(item => new NetWasmApplicationImport(
                    item.GetMetadata("Module"), item.GetMetadata("ArtifactPath"),
                    (string.IsNullOrEmpty(item.GetMetadata("Sha256"))
                        ? item.GetMetadata("FileHash") : item.GetMetadata("Sha256")).ToLowerInvariant()))]));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK066: {exception.Message}");
            return false;
        }
    }
}

public sealed class NetWasmWriteWitWorkerClientEntryTask : Microsoft.Build.Utilities.Task
{
    private readonly IWitWorkerClientWriter _writer;
    private readonly IBuildArtifactStore _artifacts;

    public NetWasmWriteWitWorkerClientEntryTask() : this(WitWorkerClientComposition.CreateWriter(), new BuildArtifactStore()) { }

    internal NetWasmWriteWitWorkerClientEntryTask(IWitWorkerClientWriter writer, IBuildArtifactStore artifacts)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    [Required] public string ContractPath { get; set; } = string.Empty;
    [Required] public string HostingJavaScriptRoot { get; set; } = string.Empty;
    [Required] public string WorkerFileName { get; set; } = string.Empty;
    [Required] public string BuildFingerprint { get; set; } = string.Empty;
    [Required] public string ManifestSha256 { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    [Required] public string DeclarationPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var source = _writer.Write(new(_artifacts.Read(Path.GetFullPath(ContractPath)),
                Path.GetFullPath(HostingJavaScriptRoot), WorkerFileName, BuildFingerprint, ManifestSha256));
            _artifacts.Write(Path.GetFullPath(OutputPath), source.JavaScript);
            _artifacts.Write(Path.GetFullPath(DeclarationPath), source.Declaration);
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK069: {exception.Message}");
            return false;
        }
    }
}

public sealed class NetWasmWriteWorkerClientEntryTask : Microsoft.Build.Utilities.Task
{
    private readonly IWorkerEntryWriter _writer;

    public NetWasmWriteWorkerClientEntryTask() : this(new WorkerEntryWriter()) { }

    internal NetWasmWriteWorkerClientEntryTask(IWorkerEntryWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string HostingJavaScriptRoot { get; set; } = string.Empty;
    [Required] public string WorkerFileName { get; set; } = string.Empty;
    [Required] public string BuildFingerprint { get; set; } = string.Empty;
    [Required] public string ManifestSha256 { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    [Required] public string DeclarationPath { get; set; } = string.Empty;
    [Required] public ITaskItem[] Exports { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var exports = Exports.Select(item => new WorkerJavaScriptExport(
                RequiredMetadata(item, "Name"),
                ReadSignature(item.GetMetadata("Parameters")),
                RequiredMetadata(item, "Result"),
                NullIfEmpty(item.GetMetadata("AsyncReturn"))))
                .ToImmutableArray();
            _writer.WriteClient(new(
                Path.GetFullPath(HostingJavaScriptRoot),
                WorkerFileName,
                BuildFingerprint,
                ManifestSha256,
                Path.GetFullPath(OutputPath),
                Path.GetFullPath(DeclarationPath),
                exports));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK062: {exception.Message}");
            return false;
        }
    }

    private static ImmutableArray<string> ReadSignature(string json)
    {
        var values = JsonSerializer.Deserialize<string[]>(json) ??
            throw new ArgumentException("Worker JavaScript signatures must be JSON arrays.");
        if (values.Any(value => value is null))
        {
            throw new ArgumentException("Worker JavaScript signatures cannot contain null.");
        }
        return [.. values];
    }

    private static string RequiredMetadata(ITaskItem item, string name)
    {
        var value = item.GetMetadata(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Worker item metadata '{name}' is required.")
            : value;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
