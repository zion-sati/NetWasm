using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ILinkedCorpusObservationResponseParser
{
    LinkedCorpusObservationResult Parse(
        string json,
        LinkedCorpusObservationRequest request,
        ImmutableDictionary<int, string> typeNames);
}

internal sealed class LinkedCorpusObservationResponseParser : ILinkedCorpusObservationResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public LinkedCorpusObservationResult Parse(
        string json,
        LinkedCorpusObservationRequest request,
        ImmutableDictionary<int, string> typeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(typeNames);
        var response = JsonSerializer.Deserialize<Response>(json, Options)
            ?? throw new JsonException("Linked observation response must be a JSON object.");
        if (response.SchemaVersion != request.SchemaVersion
            || !StringComparer.Ordinal.Equals(response.Target, request.Target)
            || !StringComparer.Ordinal.Equals(response.ModuleSha256, request.ModuleSha256)
            || !StringComparer.Ordinal.Equals(response.ManifestSha256, request.ManifestSha256))
        {
            throw new JsonException("Linked observation response identity differs from its request.");
        }
        if (response.Observations.IsDefault
            || response.Observations.Length != request.Inputs.Length)
        {
            throw new JsonException("Linked observation response count differs from its request.");
        }

        var result = ImmutableDictionary.CreateBuilder<int, OracleObservation>();
        for (var index = 0; index < request.Inputs.Length; index++)
        {
            var expectedInput = request.Inputs[index];
            var raw = response.Observations[index];
            if (raw.Input != expectedInput || result.ContainsKey(raw.Input))
            {
                throw new JsonException("Linked observations must preserve unique request order.");
            }

            var (kind, exceptionType) = raw.Kind switch
            {
                "value" when raw.Value is not null && raw.ExceptionTypeId is null =>
                    (OracleObservationKind.Value, (string?)null),
                "exception" when raw.Value is null && raw.ExceptionTypeId is > 0
                    && typeNames.TryGetValue(raw.ExceptionTypeId.Value, out var typeName) =>
                    (OracleObservationKind.ManagedException, typeName),
                "trap" when raw.Value is null && raw.ExceptionTypeId is null =>
                    (OracleObservationKind.Trap, (string?)null),
                _ => throw new JsonException("Linked observation outcome is inconsistent."),
            };
            var records = raw.TraceRecords.IsEmpty
                ? [new TraceRecord(TraceRecordKind.StateChecksum, 0, raw.Trace)]
                : raw.TraceRecords.Select(record =>
                {
                    if (!Enum.IsDefined((TraceRecordKind)record.Kind))
                    {
                        throw new JsonException("Linked observation trace kind is unknown.");
                    }
                    return new TraceRecord(
                        (TraceRecordKind)record.Kind,
                        record.EventId,
                        unchecked(((long)record.PayloadHigh << 32) |
                            (uint)record.PayloadLow));
                }).ToImmutableArray();
            result.Add(raw.Input, new(kind, raw.Value, exceptionType, raw.Trace)
            {
                TraceRecords = records,
            });
        }
        return new(result.ToImmutable(), response.ModuleSha256, response.ManifestSha256);
    }

    private sealed record Response(
        int SchemaVersion,
        string Target,
        string ModuleSha256,
        string ManifestSha256,
        ImmutableArray<RawObservation> Observations);

    private sealed record RawObservation(
        int Input,
        string Kind,
        int? Value,
        int? ExceptionTypeId,
        int Trace,
        ImmutableArray<RawTraceRecord> TraceRecords);

    private sealed record RawTraceRecord(
        int Kind,
        int EventId,
        int PayloadLow,
        int PayloadHigh);
}
