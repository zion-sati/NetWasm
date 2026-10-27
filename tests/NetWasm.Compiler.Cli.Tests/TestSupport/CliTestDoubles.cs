using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Cli.Tests.TestSupport;

internal sealed class RecordingConsole : IConsole
{
    public List<string> Lines { get; } = [];

    public void WriteLine(string line) => Lines.Add(line);
}

// Response files by path; any other path is missing, as on disk.
internal sealed class InMemoryTextFiles(Dictionary<string, string>? files = null) : ITextFileReader
{
    private readonly Dictionary<string, string> _files = files ?? [];

    public List<string> Reads { get; } = [];

    public string Read(string path)
    {
        Reads.Add(path);
        return _files.TryGetValue(path, out var text)
            ? text
            : throw new FileNotFoundException($"Could not find file '{path}'.", path);
    }
}

internal sealed class RecordingFiles : IBinaryFileWriter, ITextFileWriter, IFileDeleter
{
    public List<(string Path, byte[] Content)> BinaryWrites { get; } = [];
    public List<(string Path, string Content)> TextWrites { get; } = [];
    public List<string> Deletes { get; } = [];
    public List<string> Operations { get; } = [];

    public void Write(string path, byte[] content)
    {
        BinaryWrites.Add((path, content));
        Operations.Add($"binary:{path}");
    }

    public void Write(string path, string content)
    {
        TextWrites.Add((path, content));
        Operations.Add($"text:{path}");
    }

    public void Delete(string path)
    {
        Deletes.Add(path);
        Operations.Add($"delete:{path}");
    }
}

internal static class NwcTestArguments
{
    // Reads a command line that must hold no argument errors.
    public static NwcArguments Read(IReadOnlySet<string> options, params string[] arguments)
    {
        var read = NwcArgumentReader.Read(arguments, options, new InMemoryTextFiles());
        Assert.Empty(read.Errors);
        return read;
    }

    public static ILogger<T> ConsoleLogger<T>(IConsole console)
    {
        var factory = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new NwcConsoleLoggerProvider(console)));
        return factory.CreateLogger<T>();
    }
}
