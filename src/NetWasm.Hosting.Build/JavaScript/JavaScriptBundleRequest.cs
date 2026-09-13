namespace NetWasm.Hosting.Build.JavaScript;

public sealed record JavaScriptBundleRequest(
    string NodePath,
    string CommandPath,
    string EntryPath,
    string OutputPath,
    string Platform,
    bool Minify);

public interface IJavaScriptBundler
{
    void Bundle(JavaScriptBundleRequest request);
}
