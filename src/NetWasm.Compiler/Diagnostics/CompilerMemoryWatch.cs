// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerMemoryWatch
{
    // Watches a compile's memory until the returned handle is disposed.
    IDisposable Watch();
}

// A host that keeps its own memory policy (MSBuild's process, a test host, a browser) watches nothing.
internal sealed class UnwatchedCompilerMemory : ICompilerMemoryWatch
{
    public IDisposable Watch() => Unwatched.Instance;

    private sealed class Unwatched : IDisposable
    {
        internal static readonly Unwatched Instance = new();

        public void Dispose()
        {
        }
    }
}

// nwc's process: the releaser runs every quarter second through the compile, so Satori's committed memory goes back
// within any stage, not only where a stage calls the releaser. Emission has no such call, and with compiled units on
// Devolutions' Avalonia sample committed 46 GB there over a heap of 7 to 12 GB.
internal sealed class PollingCompilerMemoryWatch(ICompilerMemoryReleaser releaser, TimeProvider time) : ICompilerMemoryWatch
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly ICompilerMemoryReleaser _releaser = releaser ?? throw new ArgumentNullException(nameof(releaser));
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    public IDisposable Watch() => _time.CreateTimer(static releaser => ((ICompilerMemoryReleaser)releaser!).Release(),
        _releaser, Interval, Interval);
}
