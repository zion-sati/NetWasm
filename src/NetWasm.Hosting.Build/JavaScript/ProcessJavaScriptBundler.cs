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
            ],
            "JavaScript bundler"));
        if (!File.Exists(request.OutputPath))
        {
            throw new InvalidOperationException(
                "The NetWasm JavaScript bundler produced no output.");
        }
    }

    private static void Validate(JavaScriptBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var path in new[]
                 {
                     request.NodePath,
                     request.CommandPath,
                     request.EntryPath,
                     request.OutputPath,
                 })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException("JavaScript bundle paths must be absolute.", nameof(request));
            }
        }
        if (request.Platform is not ("browser" or "node"))
        {
            throw new ArgumentException(
                "The JavaScript bundle platform must be browser or node.",
                nameof(request));
        }
    }
}
