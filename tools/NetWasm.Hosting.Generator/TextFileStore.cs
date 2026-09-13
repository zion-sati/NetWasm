using System.Text;

namespace NetWasm.Hosting.Generator;

internal interface ITextFileStore
{
    string Read(string path);

    void Write(string path, string content);
}

internal sealed class TextFileStore : ITextFileStore
{
    public string Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public void Write(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(directory!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
