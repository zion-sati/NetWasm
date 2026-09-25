namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class InvalidCilReducer(TimeProvider timeProvider) :
    IInvalidCilReducer
{
    public InvalidCilReductionResult Reduce(InvalidCilReductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Original);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Invariant);
        ArgumentNullException.ThrowIfNull(request.Oracle);
        if (request.Original.Length == 0 || request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "invalid CIL reduction needs bytes and a positive timeout",
                nameof(request));
        }
        using var deadline = new CancellationTokenSource(request.Timeout);
        var started = timeProvider.GetTimestamp();
        var best = request.Original.ToArray();
        var attempts = 0;
        var chunk = Math.Max(1, best.Length / 2);
        while (chunk >= 1 && !deadline.IsCancellationRequested)
        {
            var improved = false;
            for (var start = 0; start <= best.Length - chunk; start++)
            {
                if (deadline.IsCancellationRequested ||
                    timeProvider.GetElapsedTime(started) >= request.Timeout)
                {
                    return Result(timedOut: true);
                }
                if (best.Length == chunk)
                {
                    continue;
                }
                var candidate = new byte[best.Length - chunk];
                best.AsSpan(0, start).CopyTo(candidate);
                best.AsSpan(start + chunk).CopyTo(candidate.AsSpan(start));
                attempts++;
                if (!StringComparer.Ordinal.Equals(
                        request.Invariant,
                        request.Oracle(candidate, deadline.Token)))
                {
                    continue;
                }
                best = candidate;
                improved = true;
                break;
            }
            if (!improved)
            {
                if (chunk == 1)
                {
                    break;
                }
                chunk = Math.Max(1, chunk / 2);
            }
        }
        return Result(deadline.IsCancellationRequested);

        InvalidCilReductionResult Result(bool timedOut) => new(
            request.Original.ToArray(),
            best,
            request.Invariant,
            timedOut,
            attempts);
    }
}
