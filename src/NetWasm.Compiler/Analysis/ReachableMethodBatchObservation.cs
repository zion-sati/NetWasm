using System;

namespace NetWasm.Compiler.Analysis;

internal readonly record struct ReachableMethodBatchObservation(
    int RequestCount,
    int WorkerCount,
    TimeSpan Elapsed,
    TimeSpan WorkerBusy,
    TimeSpan OrderedWait,
    int PeakBufferedResults);
