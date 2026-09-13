namespace NetWasm.Compiler.ComponentModel.Tests;

internal sealed class ComponentModelTestFiles : IDisposable
{
    public ComponentModelTestFiles()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "netwasm-component-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string PathFor(string name) => Path.Combine(DirectoryPath, name);

    public string Create(string name, params byte[] contents)
    {
        var path = PathFor(name);
        File.WriteAllBytes(path, contents);
        return path;
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
