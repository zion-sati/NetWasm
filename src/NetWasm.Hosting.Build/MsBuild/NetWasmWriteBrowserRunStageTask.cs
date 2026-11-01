using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Execution;

namespace NetWasm.Hosting.Build.MsBuild;

/// <summary>Writes the command dotnet run's development server stages a browser project's site with.</summary>
public sealed class NetWasmWriteBrowserRunStageTask : Microsoft.Build.Utilities.Task
{
    private readonly IBrowserRunStageWriter _writer;

    public NetWasmWriteBrowserRunStageTask() : this(new BrowserRunStageWriter()) { }

    internal NetWasmWriteBrowserRunStageTask(IBrowserRunStageWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string DotNetPath { get; set; } = string.Empty;
    [Required] public string ProjectPath { get; set; } = string.Empty;
    [Required] public string SiteDirectory { get; set; } = string.Empty;
    [Required] public string BrowserDirectory { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var properties = BuildEngine is IBuildEngine6 engine
                ? engine.GetGlobalProperties()
                : throw new InvalidOperationException("MSBuild does not report the run's global properties.");
            _writer.Write(new(
                DotNetPath,
                ProjectPath,
                SiteDirectory,
                BrowserDirectory,
                properties.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase),
                OutputPath));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK094: {exception.Message}");
            return false;
        }
    }
}
