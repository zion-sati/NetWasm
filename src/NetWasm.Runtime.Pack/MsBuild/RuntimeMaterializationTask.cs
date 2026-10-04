using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Runtime.Pack.Composition;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.MsBuild;

public sealed class RuntimeMaterializationTask : Task
{
    private readonly IRuntimeModuleMaterializer _materializer;
    private readonly INativeLibraryItemReader _nativeLibraries;

    public RuntimeMaterializationTask()
        : this(RuntimeMaterializationComposition.Create(), RuntimeMaterializationComposition.CreateNativeLibraryItemReader())
    {
    }

    internal RuntimeMaterializationTask(IRuntimeModuleMaterializer materializer, INativeLibraryItemReader nativeLibraries)
    {
        _materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
        _nativeLibraries = nativeLibraries ?? throw new ArgumentNullException(nameof(nativeLibraries));
    }

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    [Required]
    public string RuntimeLayoutPath { get; set; } = string.Empty;

    [Required]
    public string AssetRoot { get; set; } = string.Empty;

    [Required]
    public string WasmLdPath { get; set; } = string.Empty;

    public string WasmOptPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsNodePath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsCommandPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsModulePath { get; set; } = string.Empty;

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string LogDirectory { get; set; } = string.Empty;

    [Required]
    public string CacheDirectory { get; set; } = string.Empty;

    [Required]
    public string Target { get; set; } = string.Empty;

    public string Optimization { get; set; } = "Oz";

    public string InitialHeapSizeBytes { get; set; } = string.Empty;

    public string MaximumMemorySizeBytes { get; set; } = string.Empty;

    public ITaskItem[] NativeLibraries { get; set; } = [];

    public string NativeCallbackObjectPath { get; set; } = string.Empty;

    [Required]
    public string SdkVersion { get; set; } = string.Empty;

    [Required]
    public string CompilerVersion { get; set; } = string.Empty;

    [Required]
    public string RuntimeVersion { get; set; } = string.Empty;

    [Required]
    public string RuntimePackVersion { get; set; } = string.Empty;

    [Required]
    public string HostToolsPackageId { get; set; } = string.Empty;

    [Required]
    public string HostToolsPackageVersion { get; set; } = string.Empty;

    [Required]
    public string WasmLdVersion { get; set; } = string.Empty;

    [Required]
    public string WasmOptVersion { get; set; } = string.Empty;

    [Required]
    public string WasmToolsVersion { get; set; } = string.Empty;

    [Required]
    public string NodeVersion { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] RuntimeModules { get; private set; } = [];

    public override bool Execute()
    {
        RuntimeModules = [];
        try
        {
            var materialization = _materializer.Materialize(new(
                ManifestPath,
                RuntimeLayoutPath,
                AssetRoot,
                WasmLdPath,
                WasmOptPath,
                WasmToolsNodePath,
                WasmToolsCommandPath,
                WasmToolsModulePath,
                OutputPath,
                LogDirectory,
                CacheDirectory,
                Target,
                ParseOptimization(Optimization),
                ParseOptionalSize(InitialHeapSizeBytes),
                ParseOptionalSize(MaximumMemorySizeBytes),
                new(
                    SdkVersion,
                    CompilerVersion,
                    RuntimeVersion,
                    RuntimePackVersion,
                    HostToolsPackageId,
                    HostToolsPackageVersion,
                    WasmLdVersion,
                    WasmOptVersion,
                    WasmToolsVersion,
                    NodeVersion))
            {
                NativeLibraries = _nativeLibraries.Read(NativeLibraries),
                NativeCallbackObjectPath = NativeCallbackObjectPath,
            });
            RuntimeModules = [CreateRuntimeModule(materialization)];
            LogCacheMetrics(materialization.CacheMetrics);
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWPACK001: {exception.Message}");
            return false;
        }
    }

    private static RuntimeWasmOptimization ParseOptimization(string value) => value switch
    {
        "None" => RuntimeWasmOptimization.None,
        "O0" => RuntimeWasmOptimization.O0,
        "O1" => RuntimeWasmOptimization.O1,
        "O2" => RuntimeWasmOptimization.O2,
        "O3" => RuntimeWasmOptimization.O3,
        "Os" => RuntimeWasmOptimization.Os,
        "Oz" => RuntimeWasmOptimization.Oz,
        _ => throw new InvalidOperationException("The NetWasm optimization property is invalid."),
    };

    private static long? ParseOptionalSize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 0)
        {
            throw new InvalidOperationException("A NetWasm memory size property is invalid.");
        }

        return size;
    }

    private static TaskItem CreateRuntimeModule(RuntimeMaterialization materialization)
    {
        var item = new TaskItem(materialization.OutputPath);
        item.SetMetadata("Kind", "RuntimeModule");
        item.SetMetadata("WasmTarget", materialization.Target);
        item.SetMetadata("RuntimeGlobalBase", Format(materialization.RuntimeGlobalBase));
        item.SetMetadata("HeapBase", Format(materialization.HeapBase));
        item.SetMetadata("InitialMemorySizeBytes", Format(materialization.InitialMemorySizeBytes));
        item.SetMetadata("MaximumMemorySizeBytes", Format(materialization.MaximumMemorySizeBytes));
        item.SetMetadata("Digest", materialization.Sha256);
        item.SetMetadata("RuntimeAbi", materialization.RuntimeAbi);
        item.SetMetadata("BuildSeam", materialization.BuildSeam);
        item.SetMetadata("ToolchainFingerprint", materialization.ToolchainFingerprint);
        ((ITaskItem2)item).SetMetadataValueLiteral("InternalRuntimeExports", JsonSerializer.Serialize(materialization.InternalRuntimeExports));
        ((ITaskItem2)item).SetMetadataValueLiteral(
            "InternalApplicationExports",
            JsonSerializer.Serialize(materialization.InternalApplicationExports));
        return item;
    }

    private void LogCacheMetrics(RuntimeMaterializationCacheMetrics metrics) =>
        Log.LogMessage(
            MessageImportance.Low,
            "NetWasm cache: stage={0} key={1} outcome={2} recomputed={3} bytes={4} lookupMs={5:F3} totalMs={6:F3}",
            metrics.Stage,
            metrics.KeyPrefix,
            metrics.Outcome.ToString().ToLowerInvariant(),
            metrics.Recomputed.ToString().ToLowerInvariant(),
            metrics.Bytes,
            metrics.LookupMilliseconds,
            metrics.TotalMilliseconds);

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
