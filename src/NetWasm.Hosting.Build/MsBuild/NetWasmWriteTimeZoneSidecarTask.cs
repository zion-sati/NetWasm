// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.TimeZones;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmWriteTimeZoneSidecarTask : Microsoft.Build.Utilities.Task
{
    private readonly ITimeZoneSidecarWriter _writer;

    public NetWasmWriteTimeZoneSidecarTask() : this(new TimeZoneSidecarWriter()) { }

    internal NetWasmWriteTimeZoneSidecarTask(ITimeZoneSidecarWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string SourceRoot { get; set; } = string.Empty;
    [Required] public string Mode { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    public ITaskItem[] Zones { get; set; } = [];
    [Output] public string DataVersion { get; private set; } = string.Empty;
    [Output] public string Identity { get; private set; } = string.Empty;
    [Output] public int ZoneCount { get; private set; }

    public override bool Execute()
    {
        try
        {
            if (!Enum.TryParse<TimeZoneSidecarMode>(Mode, ignoreCase: true, out var mode)
                || !Enum.IsDefined(mode))
            {
                throw new ArgumentException($"NetWasmTimeZoneMode must be Selected or Full, not '{Mode}'.");
            }
            var sidecar = _writer.Write(new(
                Path.GetFullPath(SourceRoot),
                mode,
                [.. Zones.Select(item => item.ItemSpec)],
                Path.GetFullPath(OutputPath)));
            DataVersion = sidecar.DataVersion;
            Identity = sidecar.Identity;
            ZoneCount = sidecar.ZoneCount;
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK079: {exception.Message}");
            return false;
        }
    }
}
