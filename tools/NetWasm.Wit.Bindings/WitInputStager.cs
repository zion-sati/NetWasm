namespace NetWasm.Wit.Bindings;

internal interface IWitInputStager
{
    StagedWitInput Stage(string wit);
}

// The WIT path wasm-tools reads, and the temporary copy it may be.
internal sealed class StagedWitInput(string path, string? temporaryDirectory) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose()
    {
        if (temporaryDirectory is not null)
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}

// wasm-tools runs as a separate process and the WIT is read more than once,
// so standard input ("-") and a stream that can be read only once, such as
// the pipe a shell passes for <(command), are copied to a temporary file.
// Files and package directories are read in place.
internal sealed class WitInputStager(TextReader standardInput) : IWitInputStager
{
    private readonly TextReader _standardInput = standardInput ??
        throw new ArgumentNullException(nameof(standardInput));

    public StagedWitInput Stage(string wit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wit);
        if (wit == "-")
        {
            return Copy(_standardInput.ReadToEnd());
        }
        if (!File.Exists(wit))
        {
            return new(wit, null);
        }
        using var stream = new FileStream(wit, FileMode.Open, FileAccess.Read);
        if (stream.CanSeek)
        {
            return new(wit, null);
        }
        using var reader = new StreamReader(stream);
        return Copy(reader.ReadToEnd());
    }

    private static StagedWitInput Copy(string text)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-wit-");
        var path = System.IO.Path.Combine(directory.FullName, "input.wit");
        File.WriteAllText(path, text);
        return new(path, directory.FullName);
    }
}
