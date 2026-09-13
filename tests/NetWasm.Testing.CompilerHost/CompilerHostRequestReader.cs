using System.Text.Json;

namespace NetWasm.Testing.CompilerHost;

internal interface ICompilerHostRequestDeserializer
{
    CompilationRequest Deserialize(string json);
}

internal sealed class CompilerHostRequestDeserializer(
    JsonSerializerOptions options) : ICompilerHostRequestDeserializer
{
    private readonly JsonSerializerOptions _options = options ??
        throw new ArgumentNullException(nameof(options));

    public CompilationRequest Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<CompilationRequest>(json, _options)
            ?? throw new InvalidOperationException("compiler request was empty");
    }
}

internal sealed class CompilerHostRequestReader(
    ICompilerHostRequestDeserializer deserializer)
{
    private readonly ICompilerHostRequestDeserializer _deserializer = deserializer ??
        throw new ArgumentNullException(nameof(deserializer));

    public CompilationRequest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _deserializer.Deserialize(File.ReadAllText(path));
    }
}
