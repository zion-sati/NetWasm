using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Build;

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

    // The shared runtime an entry's netwasm:runtime/ imports resolve to, and
    // the relative URL of its directory from the bundle's.
    public string RuntimeManifestPath { get; set; } = string.Empty;
    public string RuntimeUrlPrefix { get; set; } = "./";

    [Output] public string BundlePath { get; private set; } = string.Empty;

    // The runtime files the bundle loads, each with its RelativePath.
    [Output] public ITaskItem[] RuntimeFiles { get; private set; } = [];

    public override bool Execute()
    {
        RuntimeFiles = [];
        try
        {
            var request = new JavaScriptBundleRequest(
                Path.GetFullPath(NodePath),
                Path.GetFullPath(CommandPath),
                Path.GetFullPath(EntryPath),
                Path.GetFullPath(OutputPath),
                Platform,
                Minify);
            var runtimeFiles = Array.Empty<ITaskItem>();
            if (RuntimeManifestPath.Length != 0)
            {
                (request, runtimeFiles) = UseRuntime(request);
            }
            _bundler.Bundle(request);
            BundlePath = request.OutputPath;
            RuntimeFiles = runtimeFiles;
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK041: {exception.Message}");
            return false;
        }
    }

    private (JavaScriptBundleRequest Request, ITaskItem[] Files) UseRuntime(JavaScriptBundleRequest request)
    {
        if (RuntimeUrlPrefix != "./" && !RuntimeUrlPrefix.Split('/')[..^1].All(segment => segment == "..")
            || !RuntimeUrlPrefix.EndsWith('/'))
        {
            throw new ArgumentException("The JavaScript runtime URL prefix must be ./ or a sequence of ../.");
        }
        var manifestPath = Path.GetFullPath(RuntimeManifestPath);
        var modules = JavaScriptRuntimeManifest.Read(manifestPath);
        var used = JavaScriptRuntimeManifest.FindImports(File.ReadAllText(request.EntryPath));
        var missing = used.FirstOrDefault(module => !modules.ContainsKey(module));
        if (missing is not null)
        {
            throw new InvalidOperationException($"The JavaScript runtime has no module '{missing}'.");
        }
        var importsPath = request.EntryPath + ".runtime-imports.json";
        File.WriteAllText(importsPath, JsonSerializer.Serialize(
            used.ToDictionary(
                JavaScriptRuntime.Specifier,
                module => RuntimeUrlPrefix + modules[module].File,
                StringComparer.Ordinal),
            HostingBuildJsonContext.Default.DictionaryStringString));
        var directory = Path.GetDirectoryName(manifestPath)!;
        var files = used.SelectMany(module => modules[module].Files)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(file =>
            {
                var item = new TaskItem(Path.Combine(directory, file));
                item.SetMetadata("RelativePath", file);
                return (ITaskItem)item;
            })
            .ToArray();
        return (request with { RuntimeImportsPath = importsPath }, files);
    }
}
