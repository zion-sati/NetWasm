using System.Threading;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// One request's analysis: queued, then claimed by exactly one thread, then done or, when
/// its session ends first, cancelled. The result is written before the state becomes done
/// with a full fence, so a thread that reads done with a volatile read sees the result.
/// </summary>
internal sealed class ReachableMethodAnalysisSlot(ReachableMethodRequest request)
{
    private const int Queued = 0;
    private const int Running = 1;
    private const int Done = 2;
    private const int Cancelled = 3;

    private int _state;

    internal ReachableMethodRequest Request { get; } = request;

    internal ReachableMethodAnalysis? Analysis { get; set; }

    internal System.Exception? Failure { get; set; }

    internal bool IsDone => Volatile.Read(ref _state) == Done;

    internal bool TryClaim() => Interlocked.CompareExchange(ref _state, Running, Queued) == Queued;

    internal bool TryCancel() => Interlocked.CompareExchange(ref _state, Cancelled, Queued) == Queued;

    internal void Complete() => Interlocked.Exchange(ref _state, Done);
}
