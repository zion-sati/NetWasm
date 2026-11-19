// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Core;

/// <summary>Immutable host selection shared by compiler and Wasm composition.</summary>
// ThreadsAvailable: whether the host can start threads. A browser host cannot, so a compile there runs on the
// calling thread throughout.
public sealed record CompilerParallelism(int WorkerCount, bool ThreadsAvailable = true)
{
    // Root maps and body emission stop gaining past eight workers on fui-cs's page, while
    // each further worker still costs its own layout fork.
    private const int MaximumWorkers = 8;

    public static CompilerParallelism ForHost(
        bool isBrowser, int processorCount) =>
        new(isBrowser ? 1 : Math.Min(MaximumWorkers, Math.Max(1, processorCount)), ThreadsAvailable: !isBrowser);
}
