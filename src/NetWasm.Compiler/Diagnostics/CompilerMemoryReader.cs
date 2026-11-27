// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;

namespace NetWasm.Compiler.Diagnostics;

// The compiler process's memory, in bytes: its working set, the most it has had, what the managed heap holds, and, as
// of the last collection, the memory the collector had committed and the free space between the heap's objects. A host
// with no process to ask, such as a browser, reports zero working sets.
internal readonly record struct CompilerMemorySnapshot(
    long WorkingSet,
    long PeakWorkingSet,
    long ManagedHeap,
    long Committed,
    long Fragmented);

internal interface ICompilerMemoryReader
{
    CompilerMemorySnapshot Read();
}

// The compiler's own memory: its process's working sets where it runs in one, and its managed heap everywhere. In a
// browser or under WASI (hasProcess false) there is no process to read.
internal sealed class ProcessCompilerMemoryReader(bool hasProcess) : ICompilerMemoryReader
{
    private long _peakWorkingSet;

    public CompilerMemorySnapshot Read()
    {
        var managedHeap = GC.GetTotalMemory(forceFullCollection: false);
        var collected = GC.GetGCMemoryInfo();
        if (!hasProcess) return new(0, 0, managedHeap, collected.TotalCommittedBytes, collected.FragmentedBytes);
        using var process = Process.GetCurrentProcess();
        var workingSet = process.WorkingSet64;
        // macOS reports no peak working set (PeakWorkingSet64 is 0 there), so the peak is also the largest working set
        // this reader has seen; where the system reports one, that is never below it.
        _peakWorkingSet = Math.Max(_peakWorkingSet, Math.Max(process.PeakWorkingSet64, workingSet));
        return new(workingSet, _peakWorkingSet, managedHeap, collected.TotalCommittedBytes, collected.FragmentedBytes);
    }
}
