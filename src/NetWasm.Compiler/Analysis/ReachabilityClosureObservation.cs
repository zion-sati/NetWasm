using System;

namespace NetWasm.Compiler.Analysis;

internal readonly record struct ReachabilityClosureObservation(
    int MethodRequestCount,
    TimeSpan MethodPublication,
    int DispatchCount,
    int ResolvedDispatchCount,
    TimeSpan DispatchPublication,
    TimeSpan Elapsed);
