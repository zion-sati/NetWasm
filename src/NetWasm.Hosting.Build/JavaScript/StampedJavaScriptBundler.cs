using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Hosting.Build.Environment;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// Leaves an entry bundle in place when bundling again would write the same file: the
/// request, Node, the bundler, and every file the last bundle read are unchanged. The
/// bundler lists those files beside the bundle; without the list, it bundles again.
/// </summary>
public sealed class StampedJavaScriptBundler(IJavaScriptBundler bundler) : IJavaScriptBundler
{
    private readonly IJavaScriptBundler _bundler = bundler ?? throw new ArgumentNullException(nameof(bundler));

    public void Bundle(JavaScriptBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stampPath = request.OutputPath + ".stamp";
        if (File.Exists(request.OutputPath) && File.Exists(stampPath) &&
            Stamp(request) is { } current && File.ReadAllText(stampPath) == current)
        {
            return;
        }
        // A bundle that fails leaves neither file, so the next build bundles again.
        foreach (var path in new[] { stampPath, InputsPath(request) })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        _bundler.Bundle(request);
        if (Stamp(request) is { } stamp)
        {
            File.WriteAllText(stampPath, stamp);
        }
    }

    public void BundleRuntime(JavaScriptRuntimeBundleRequest request) => _bundler.BundleRuntime(request);

    // Null without a readable list of the files the last bundle read.
    private static string? Stamp(JavaScriptBundleRequest request)
    {
        string[]? inputs;
        try
        {
            var path = InputsPath(request);
            if (!File.Exists(path))
            {
                return null;
            }
            inputs = JsonSerializer.Deserialize(File.ReadAllBytes(path), HostingBuildJsonContext.Default.StringArray);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
        if (inputs is null || inputs.Any(input => !Path.IsPathFullyQualified(input)))
        {
            return null;
        }
        // The build writes the entry and the modules it imports again on every publish, so the
        // bundle's own inputs, which are small, are compared by content rather than write time.
        var contents = new List<string?>
        {
            typeof(StampedJavaScriptBundler).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
            request.EntryPath, request.OutputPath, request.Platform, request.Minify ? "minified" : "readable",
            request.RuntimeImportsPath,
        };
        foreach (var input in inputs.Prepend(request.EntryPath).Append(request.RuntimeImportsPath))
        {
            contents.Add(input);
            contents.Add(input is not null && File.Exists(input)
                ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input)))
                : null);
        }
        return FileTreeStamp.Compute(contents, [request.NodePath, Path.GetDirectoryName(request.CommandPath)]);
    }

    private static string InputsPath(JavaScriptBundleRequest request) => request.OutputPath + ".inputs.json";
}
