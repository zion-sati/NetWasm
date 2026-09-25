using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record CorpusCompilerResponse(
    ImmutableDictionary<int, string> TypeNames,
    string ModuleSha256,
    int StaticDataEnd,
    TimeSpan? AdapterDuration = null,
    JsonElement? CompilerTiming = null,
    JsonElement? CompilerMetrics = null);

internal interface ICorpusCompilerResponseParser
{
    CorpusCompilerResponse Parse(string json);
}

internal sealed class CorpusCompilerResponseParser : ICorpusCompilerResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public CorpusCompilerResponse Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<CorpusCompilerResponse>(json, Options)
            ?? throw new JsonException("Compiler response must be a JSON object.");
    }
}
