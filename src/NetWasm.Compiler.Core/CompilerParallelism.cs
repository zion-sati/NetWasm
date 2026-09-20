using System;

namespace NetWasm.Compiler.Core;

/// <summary>Immutable host selection shared by compiler and Wasm composition.</summary>
public sealed record CompilerParallelism(int WorkerCount)
{
    public static CompilerParallelism ForHost(
        bool isBrowser, int processorCount) =>
        new(isBrowser ? 1 : Math.Min(4, Math.Max(1, processorCount)));
}
