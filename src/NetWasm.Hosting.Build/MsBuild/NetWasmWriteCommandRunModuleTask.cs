// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmWriteCommandRunModuleTask : Microsoft.Build.Utilities.Task
{
    private readonly ICommandRunModuleWriter _writer;

    public NetWasmWriteCommandRunModuleTask() : this(new CommandRunModuleWriter()) { }

    internal NetWasmWriteCommandRunModuleTask(ICommandRunModuleWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string BundleFileName { get; set; } = string.Empty;
    [Required] public string BuildFingerprint { get; set; } = string.Empty;
    [Required] public string ManifestSha256 { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    [Required] public string DeclarationPath { get; set; } = string.Empty;
    public ITaskItem[] Arguments { get; set; } = [];
    public ITaskItem[] EnvironmentVariables { get; set; } = [];
    public ITaskItem[] Clocks { get; set; } = [];
    [Required] public string Network { get; set; } = string.Empty;
    public bool Randomness { get; set; }

    public override bool Execute()
    {
        try
        {
            _writer.Write(new(
                BundleFileName,
                BuildFingerprint,
                ManifestSha256,
                Path.GetFullPath(OutputPath),
                Path.GetFullPath(DeclarationPath),
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
            Log.LogError($"NWSDK082: {exception.Message}");
            return false;
        }
    }
}
