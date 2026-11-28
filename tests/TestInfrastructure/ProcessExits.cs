// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Compiler;

// Waits for a program a test runs (wasmtime with a compiled component) to exit, and kills it after a limit: a
// miscompile that never finishes fails its test instead of holding the run.
internal static class ProcessExits
{
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

    public static async Task WithinLimit(Process process, string what)
    {
        ArgumentNullException.ThrowIfNull(process);
        using var limit = new CancellationTokenSource(Limit);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{what} ran for more than {Limit.TotalMinutes} minutes and was stopped.");
        }
    }
}
