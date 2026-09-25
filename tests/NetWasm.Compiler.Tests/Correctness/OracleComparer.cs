namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class OracleComparer : IOracleComparer
{
    public OracleComparison Compare(
        OracleObservation desktop,
        OracleObservation netWasm)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(netWasm);
        if (netWasm.Kind == OracleObservationKind.Trap)
        {
            return new(false, "NetWasm produced a raw trap: " + netWasm.Detail);
        }
        if (desktop.Kind == OracleObservationKind.TimedOut ||
            netWasm.Kind == OracleObservationKind.TimedOut)
        {
            return new(false,
                $"execution timed out: desktop={desktop.Kind}, " +
                $"netwasm={netWasm.Kind}");
        }
        if (desktop.Kind == OracleObservationKind.CompileRejected ||
            netWasm.Kind == OracleObservationKind.CompileRejected)
        {
            return new(false,
                $"compilation was rejected: desktop={desktop.Kind}, " +
                $"netwasm={netWasm.Kind}");
        }
        if (desktop.Kind != netWasm.Kind)
        {
            return new(false,
                $"observation kind differs: desktop={desktop.Kind}, netwasm={netWasm.Kind}");
        }
        if (desktop.Value != netWasm.Value)
        {
            return new(false,
                $"value differs: desktop={desktop.Value}, netwasm={netWasm.Value}");
        }
        if (!StringComparer.Ordinal.Equals(
                desktop.ExceptionType, netWasm.ExceptionType))
        {
            return new(false,
                "managed exception differs: " +
                $"desktop={desktop.ExceptionType}, netwasm={netWasm.ExceptionType}");
        }
        if (!desktop.TraceRecords.SequenceEqual(netWasm.TraceRecords))
        {
            var count = Math.Min(
                desktop.TraceRecords.Length,
                netWasm.TraceRecords.Length);
            var index = 0;
            while (index < count &&
                   desktop.TraceRecords[index] == netWasm.TraceRecords[index])
            {
                index++;
            }
            var expected = index < desktop.TraceRecords.Length
                ? desktop.TraceRecords[index].ToString()
                : "<end>";
            var actual = index < netWasm.TraceRecords.Length
                ? netWasm.TraceRecords[index].ToString()
                : "<end>";
            return new(false,
                $"trace differs at event {index}: desktop={expected}, " +
                $"netwasm={actual}");
        }
        return new(true, "observations are equivalent");
    }
}
