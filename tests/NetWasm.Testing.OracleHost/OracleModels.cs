namespace NetWasm.Testing.OracleHost;

internal sealed record OracleInputs(int[] Values, bool IsBatched);

internal sealed record Observation(
    string Kind,
    int? Value,
    string? ExceptionType,
    int Trace,
    string? Detail,
    TraceRecord[] TraceRecords);

internal sealed record TraceRecord(
    int Kind,
    int EventId,
    int PayloadLow,
    int PayloadHigh);
