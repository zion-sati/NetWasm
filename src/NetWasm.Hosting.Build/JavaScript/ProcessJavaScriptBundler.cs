namespace NetWasm.Hosting.Build.JavaScript;

public sealed class ProcessJavaScriptBundler : IJavaScriptBundler
{
    private readonly IJavaScriptProcessRunner _process;

    public ProcessJavaScriptBundler()
        : this(new JavaScriptProcessRunner())
    {
    }

    internal ProcessJavaScriptBundler(IJavaScriptProcessRunner process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
    }

    public void Bundle(JavaScriptBundleRequest request)
    {
        Validate(request);
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        _process.Run(new(
            request.NodePath,
            request.CommandPath,
            [
                "bundle",
                request.EntryPath,
                "--output",
                request.OutputPath,
                "--platform",
                request.Platform,
                "--minify",
                request.Minify ? "true" : "false",
                .. request.RuntimeImportsPath is null ? [] : new[] { "--runtime", request.RuntimeImportsPath },
            ],
            "JavaScript bundler"));
        if (!File.Exists(request.OutputPath))
        {
            throw new InvalidOperationException(
                "The NetWasm JavaScript bundler produced no output.");
        }
    }

    public void BundleRuntime(JavaScriptRuntimeBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAbsolute(request.NodePath, request.CommandPath, request.FacadeDirectory, request.OutputDirectory);
        _process.Run(new(
            request.NodePath,
            request.CommandPath,
            [
                "bundle-runtime",
                request.FacadeDirectory,
                "--output",
                request.OutputDirectory,
                "--minify",
                request.Minify ? "true" : "false",
            ],
            "JavaScript runtime bundler"));
        if (!File.Exists(Path.Combine(request.OutputDirectory, "runtime.json")))
        {
            throw new InvalidOperationException(
                "The NetWasm JavaScript runtime bundler produced no runtime manifest.");
        }
    }

    private static void Validate(JavaScriptBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAbsolute(request.NodePath, request.CommandPath, request.EntryPath, request.OutputPath);
        if (request.RuntimeImportsPath is not null)
        {
            RequireAbsolute(request.RuntimeImportsPath);
        }
        if (request.Platform is not ("browser" or "node"))
        {
            throw new ArgumentException(
                "The JavaScript bundle platform must be browser or node.",
                nameof(request));
        }
    }

    private static void RequireAbsolute(params string[] paths)
    {
        foreach (var path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException("JavaScript bundle paths must be absolute.", nameof(paths));
            }
        }
    }
}
