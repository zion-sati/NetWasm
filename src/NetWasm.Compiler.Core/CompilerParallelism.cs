using System;

namespace NetWasm.Compiler.Core;

/// <summary>Immutable host selection shared by compiler and Wasm composition.</summary>
public sealed record CompilerParallelism(int WorkerCount)
{
    // Root maps and body emission stop gaining past eight workers on fui-cs's page, while
    // each further worker still costs its own layout fork.
    private const int MaximumWorkers = 8;

    public static CompilerParallelism ForHost(
        bool isBrowser, int processorCount) =>
        new(isBrowser ? 1 : Math.Min(MaximumWorkers, Math.Max(1, processorCount)));
}
