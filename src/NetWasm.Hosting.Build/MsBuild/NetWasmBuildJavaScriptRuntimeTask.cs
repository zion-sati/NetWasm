using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.MsBuild;

/// <summary>
/// Bundles the shared JavaScript runtime the project's browser hosts import.
/// Every project of a release writes the same files, so a page and its
/// workers download one copy.
/// </summary>
public sealed class NetWasmBuildJavaScriptRuntimeTask : Microsoft.Build.Utilities.Task
{
    private readonly IJavaScriptBundler _bundler;

    public NetWasmBuildJavaScriptRuntimeTask()
        : this(new ProcessJavaScriptBundler())
    {
    }

    internal NetWasmBuildJavaScriptRuntimeTask(IJavaScriptBundler bundler)
    {
        _bundler = bundler ?? throw new ArgumentNullException(nameof(bundler));
    }

    [Required] public string NodePath { get; set; } = string.Empty;
    [Required] public string CommandPath { get; set; } = string.Empty;
    [Required] public string HostingJavaScriptRoot { get; set; } = string.Empty;
    [Required] public string Preview2ShimRoot { get; set; } = string.Empty;
    [Required] public string OutputDirectory { get; set; } = string.Empty;
    public bool Minify { get; set; }

    [Output] public string ManifestPath { get; private set; } = string.Empty;

    public override bool Execute()
    {
        ManifestPath = string.Empty;
        try
        {
            var root = Path.GetFullPath(OutputDirectory);
            var facades = Path.Combine(root, "facades");
            JavaScriptRuntime.WriteFacades(
                facades,
                Path.GetFullPath(HostingJavaScriptRoot),
                Path.GetFullPath(Preview2ShimRoot));
            // Each build replaces the files, so no runtime of an earlier build
            // can be published beside the current one.
            var output = Path.Combine(root, Minify ? "minified" : "readable");
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
            _bundler.BundleRuntime(new(
                Path.GetFullPath(NodePath),
                Path.GetFullPath(CommandPath),
                facades,
                output,
                Minify));
            var manifest = Path.Combine(output, "runtime.json");
            JavaScriptRuntimeManifest.Read(manifest);
            ManifestPath = manifest;
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK091: {exception.Message}");
            return false;
        }
    }
}
