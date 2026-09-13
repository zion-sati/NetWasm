using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmBundleJavaScriptTask : Microsoft.Build.Utilities.Task
{
    private readonly IJavaScriptBundler _bundler;

    public NetWasmBundleJavaScriptTask()
        : this(new ProcessJavaScriptBundler())
    {
    }

    internal NetWasmBundleJavaScriptTask(IJavaScriptBundler bundler)
    {
        _bundler = bundler ?? throw new ArgumentNullException(nameof(bundler));
    }

    [Required] public string NodePath { get; set; } = string.Empty;
    [Required] public string CommandPath { get; set; } = string.Empty;
    [Required] public string EntryPath { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;
    [Required] public string Platform { get; set; } = string.Empty;
    public bool Minify { get; set; }

    [Output] public string BundlePath { get; private set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            _bundler.Bundle(new(
                Path.GetFullPath(NodePath),
                Path.GetFullPath(CommandPath),
                Path.GetFullPath(EntryPath),
                Path.GetFullPath(OutputPath),
                Platform,
                Minify));
            BundlePath = Path.GetFullPath(OutputPath);
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK041: {exception.Message}");
            return false;
        }
    }
}
