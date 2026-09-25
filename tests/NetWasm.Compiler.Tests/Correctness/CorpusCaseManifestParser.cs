using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCaseManifestParser
{
    CorpusCaseManifest Parse(string json);
}

internal sealed class CorpusCaseManifestParser : ICorpusCaseManifestParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters =
        {
            new JsonStringEnumConverter<CorpusInputKind>(allowIntegerValues: false),
            new JsonStringEnumConverter<OracleMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<CorpusMatrixProfile>(allowIntegerValues: false),
            new JsonStringEnumConverter<CorpusExecutionBackend>(allowIntegerValues: false),
            new JsonStringEnumConverter<OracleRuntimeCapabilities>(allowIntegerValues: false),
        },
    };

    public CorpusCaseManifest Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<CorpusCaseManifest>(json, Options)
            ?? throw new JsonException("A corpus case manifest must be a JSON object.");
    }
}
