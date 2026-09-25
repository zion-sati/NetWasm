using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ILinkedCorpusObservationRequestWriter
{
    void Write(string path, LinkedCorpusObservationRequest request);
}

internal sealed class LinkedCorpusObservationRequestWriter : ILinkedCorpusObservationRequestWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Write(string path, LinkedCorpusObservationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        File.WriteAllText(path, JsonSerializer.Serialize(request, Options));
    }
}
