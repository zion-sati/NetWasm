namespace NetWasm.Hosting.Build.JavaScript;

public sealed record JavaScriptBundleRequest(
    string NodePath,
    string CommandPath,
    string EntryPath,
    string OutputPath,
    string Platform,
    bool Minify)
{
    /// <summary>
    /// The map from each <c>netwasm:runtime/</c> specifier the entry imports to
    /// the URL it reaches that runtime module by, or null for an entry that
    /// imports none.
    /// </summary>
    public string? RuntimeImportsPath { get; init; }
}

/// <summary>Bundles the shared runtime's facades into an output directory.</summary>
public sealed record JavaScriptRuntimeBundleRequest(
    string NodePath,
    string CommandPath,
    string FacadeDirectory,
    string OutputDirectory,
    bool Minify);

public interface IJavaScriptBundler
{
    void Bundle(JavaScriptBundleRequest request);

    void BundleRuntime(JavaScriptRuntimeBundleRequest request);
}
