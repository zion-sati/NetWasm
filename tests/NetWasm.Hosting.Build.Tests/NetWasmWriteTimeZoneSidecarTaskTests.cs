// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Hosting.Build.MsBuild;
using NetWasm.Hosting.Build.TimeZones;

namespace NetWasm.Hosting.Build.Tests;

public sealed class NetWasmWriteTimeZoneSidecarTaskTests
{
    [Theory]
    [InlineData("Selected", TimeZoneSidecarMode.Selected)]
    [InlineData("full", TimeZoneSidecarMode.Full)]
    public void PassesTheSelectionAndPublishesTheSidecarIdentity(string mode, TimeZoneSidecarMode expected)
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWriteTimeZoneSidecarTask(writer)
        {
            SourceRoot = "zoneinfo",
            Mode = mode,
            OutputPath = "App.wasm.tz-info",
            Zones = [new TaskItem("Australia/Melbourne")],
        };

        Assert.True(Assert.IsAssignableFrom<ITask>(task).Execute());

        Assert.Equal(new TimeZoneSidecarRequest(Path.GetFullPath("zoneinfo"), expected,
            writer.Request!.Zones, Path.GetFullPath("App.wasm.tz-info")), writer.Request);
        Assert.Equal(["Australia/Melbourne"], writer.Request.Zones);
        Assert.Equal("2026c", task.DataVersion);
        Assert.Equal("identity", task.Identity);
        Assert.Equal(2, task.ZoneCount);
    }

    [Theory]
    [InlineData("Some")]
    [InlineData("7")]
    public void RejectsUnknownModesWithADiagnostic(string mode)
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmWriteTimeZoneSidecarTask(new RecordingWriter())
        {
            BuildEngine = build,
            SourceRoot = "zoneinfo",
            Mode = mode,
            OutputPath = "App.wasm.tz-info",
        };

        Assert.False(task.Execute());
        Assert.Contains("NWSDK079", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsWriterFailuresAndRequiresAWriter()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmWriteTimeZoneSidecarTask(new RecordingWriter(new ArgumentException("unknown zone")))
        {
            BuildEngine = build,
            SourceRoot = "zoneinfo",
            Mode = "Selected",
            OutputPath = "App.wasm.tz-info",
        };

        Assert.False(task.Execute());
        Assert.Equal("NWSDK079: unknown zone", Assert.Single(build.Errors));
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteTimeZoneSidecarTask(null!));
        Assert.IsAssignableFrom<ITask>(new NetWasmWriteTimeZoneSidecarTask());
    }

    private sealed class RecordingWriter(Exception? failure = null) : ITimeZoneSidecarWriter
    {
        public TimeZoneSidecarRequest? Request { get; private set; }

        public TimeZoneSidecar Write(TimeZoneSidecarRequest request)
        {
            Request = request;
            if (failure is not null) throw failure;
            return new("2026c", "identity", 2);
        }
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => true;
    }
}
