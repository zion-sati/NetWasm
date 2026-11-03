using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Build.Execution;

/// <summary>
/// What dotnet run on a browser project publishes before serving it: the
/// project, the directory the development server serves, the browser files'
/// directory under it, and the global properties of the run, which the
/// publish takes as dotnet publish would.
/// </summary>
public sealed record BrowserRunStageRequest(
    string DotNetPath,
    string ProjectPath,
    string SiteDirectory,
    string BrowserDirectory,
    ImmutableSortedDictionary<string, string> GlobalProperties,
    string OutputPath);

/// <summary>The command the development server runs to stage the site.</summary>
public sealed record BrowserRunStage(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("arguments")] string[] Arguments);

public interface IBrowserRunStageWriter
{
    void Write(BrowserRunStageRequest request);
}

/// <summary>
/// Writes the staging command for the development server, which runs it in a
/// process of its own: dotnet run computes its run command inside the .NET
/// CLI, where MSBuild cannot start the task hosts the NetWasm build uses.
/// </summary>
public sealed class BrowserRunStageWriter : IBrowserRunStageWriter
{
    // The staging publish sets these itself. It publishes on purpose, so a project
    // that sets IsPublishable=false runs too.
    private static readonly ImmutableHashSet<string> Staged = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase, "IsPublishable", "NetWasmBrowserPublishDir", "NoBuild", "PublishDir", "_NetWasmRunStaging");

    public void Write(BrowserRunStageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var path in new[] { request.DotNetPath, request.ProjectPath, request.SiteDirectory, request.OutputPath })
        {
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException("The browser run staging paths must be absolute.", nameof(request));
        }
        if (Path.IsPathRooted(request.BrowserDirectory) || request.BrowserDirectory.Split('/', '\\').Contains(".."))
            throw new ArgumentException("The browser directory must be relative to the site directory.", nameof(request));
        var site = Path.TrimEndingDirectorySeparator(request.SiteDirectory);
        string[] arguments =
        [
            "msbuild", request.ProjectPath, "-t:Publish", "-nologo", "-v:q",
            .. request.GlobalProperties.Where(property => !Staged.Contains(property.Key))
                .Select(property => Property(property.Key, property.Value)),
            Property("IsPublishable", "true"),
            Property("NoBuild", "true"),
            Property("PublishDir", site + Path.DirectorySeparatorChar),
            Property("NetWasmBrowserPublishDir", Path.Combine(site, request.BrowserDirectory)),
            Property("_NetWasmRunStaging", "true"),
        ];
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        File.WriteAllText(request.OutputPath, JsonSerializer.Serialize(
            new BrowserRunStage(request.DotNetPath, arguments), HostingBuildJsonContext.Default.BrowserRunStage));
    }

    // MSBuild splits a command-line property at a semicolon or comma and
    // unescapes %XX, so a value passes through escaped.
    private static string Property(string name, string value) =>
        $"-p:{name}={value.Replace("%", "%25").Replace(";", "%3B").Replace(",", "%2C")}";
}
