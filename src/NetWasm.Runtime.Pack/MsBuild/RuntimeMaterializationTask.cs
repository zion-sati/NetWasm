using System;
using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Runtime.Pack.Composition;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.MsBuild;

public sealed class RuntimeMaterializationTask : Task
{
    private readonly IRuntimeModuleMaterializer _materializer;

    public RuntimeMaterializationTask()
        : this(RuntimeMaterializationComposition.Create())
    {
    }

    internal RuntimeMaterializationTask(IRuntimeModuleMaterializer materializer)
    {
        _materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
    }

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    [Required]
    public string RuntimeLayoutPath { get; set; } = string.Empty;

    [Required]
    public string AssetRoot { get; set; } = string.Empty;

    [Required]
    public string EmscriptenRoot { get; set; } = string.Empty;

    [Required]
    public string EmscriptenCacheRoot { get; set; } = string.Empty;

    [Required]
    public string WasmLdPath { get; set; } = string.Empty;

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
    public string Target { get; set; } = string.Empty;

    public string InitialHeapSizeBytes { get; set; } = string.Empty;

    public string MaximumMemorySizeBytes { get; set; } = string.Empty;

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
                EmscriptenRoot,
                EmscriptenCacheRoot,
                WasmLdPath,
                WasmToolsNodePath,
                WasmToolsCommandPath,
                WasmToolsModulePath,
                OutputPath,
                LogDirectory,
                Target,
                ParseOptionalSize(InitialHeapSizeBytes),
                ParseOptionalSize(MaximumMemorySizeBytes)));
            RuntimeModules = [CreateRuntimeModule(materialization)];
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWPACK001: {exception.Message}");
            return false;
        }
    }

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
        return item;
    }

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
