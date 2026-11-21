// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class LargeStackThreadsTests
{
    // About 64 MB of frames: far past a thread pool thread's 1.5 MB, well inside the 256 MB reserved.
    private const int Depth = 64 * 1024;

    [Fact]
    public void EveryIndexRunsOnALargeStackThreadWithRoomForDeepRecursion()
    {
        var ran = new ConcurrentDictionary<int, bool>();

        LargeStackThreads.Run(3, index =>
        {
            ran[index] = LargeStackThreads.IsCurrentThread;
            Assert.Equal(Depth, Recurse(Depth));
        });

        Assert.Equal([0, 1, 2], ran.Keys.Order());
        Assert.All(ran.Values, Assert.True);
        Assert.False(LargeStackThreads.IsCurrentThread);
    }

    // Compilations side by side, as a test host runs them, share the threads: there are never more than processors
    // (two on one processor), however much work is queued.
    [Fact]
    public void ThreadsNeverOutnumberTheProcessors()
    {
        var threads = new ConcurrentDictionary<int, bool>();
        var maximum = Math.Max(2, Environment.ProcessorCount);

        LargeStackThreads.Run(4 * maximum, _ =>
        {
            threads[Environment.CurrentManagedThreadId] = true;
            Thread.Sleep(10);
        });

        Assert.InRange(threads.Count, 1, maximum);
    }

    // A large-stack thread runs its own items, so it never waits for a thread, only for items other threads run.
    [Fact]
    public void ALargeStackCallerRunsItsOwnItems()
    {
        var (caller, item) = LargeStackThreads.Call(() =>
        {
            var ranOn = 0;
            LargeStackThreads.Run(1, _ => ranOn = Environment.CurrentManagedThreadId);
            return (Environment.CurrentManagedThreadId, ranOn);
        });

        Assert.Equal(caller, item);
    }

    [Fact]
    public void ACallReturnsItsResultFromALargeStackThread()
    {
        var caller = Environment.CurrentManagedThreadId;

        var (thread, depth) = LargeStackThreads.Call(() => (Environment.CurrentManagedThreadId, Recurse(Depth)));

        Assert.NotEqual(caller, thread);
        Assert.Equal(Depth, depth);
        // A call from a large-stack thread stays on it.
        var (outer, inner) = LargeStackThreads.Call(() =>
            (Environment.CurrentManagedThreadId, LargeStackThreads.Call(() => Environment.CurrentManagedThreadId)));
        Assert.Equal(outer, inner);
    }

    // Work runs in the caller's execution context, and without one when the caller suppressed its flow.
    [Fact]
    public void WorkRunsInTheCallersExecutionContextUnlessItsFlowIsSuppressed()
    {
        var context = new AsyncLocal<string?> { Value = "compilation" };
        string? flowed = null;
        string? suppressed = "unset";

        LargeStackThreads.Run(1, _ => flowed = context.Value);
        using (ExecutionContext.SuppressFlow())
        {
            LargeStackThreads.Run(1, _ => suppressed = context.Value);
        }

        Assert.Equal("compilation", flowed);
        Assert.Null(suppressed);
    }

    // A thread keeps nothing of the context of the call that started it: work whose caller suppressed flow, run on
    // it later, sees no context. Here one call, with a value in its context, starts every thread.
    [Fact]
    public void AThreadKeepsNoContextOfTheCallThatStartedIt()
    {
        var run = FreshThreads();
        var context = new AsyncLocal<string?> { Value = "compilation" };
        string? suppressed = "unset";

        RunOnEveryThread(run, _ => { });
        using (ExecutionContext.SuppressFlow())
        {
            run(1, _ => suppressed = context.Value);
        }

        Assert.Null(suppressed);
    }

    // What work whose caller suppressed flow sets in its context ends with the work: the next such work on the same
    // thread sees none of it.
    [Fact]
    public void WhatWorkWithoutAContextSetsEndsWithTheWork()
    {
        var run = FreshThreads();
        var context = new AsyncLocal<string?>();
        var seen = new ConcurrentBag<string?>();

        using (ExecutionContext.SuppressFlow())
        {
            RunOnEveryThread(run, _ => context.Value = "left behind");
            RunOnEveryThread(run, _ => seen.Add(context.Value));
        }

        Assert.Equal(Threads, seen.Count);
        Assert.All(seen, Assert.Null);
    }

    private static readonly int Threads = Math.Max(2, Environment.ProcessorCount);

    // One item on each thread: the items wait until all of them run, so each holds its own thread, and on threads
    // that do not exist yet the call starts every one.
    private static void RunOnEveryThread(Action<int, Action<int>> run, Action<int> body)
    {
        using var allRunning = new Barrier(Threads);
        run(Threads, index =>
        {
            body(index);
            Assert.True(allRunning.SignalAndWait(TimeSpan.FromMinutes(1)));
        });
    }

    // Run of a copy of the type in a load context of its own, which has started no thread yet.
    private static Action<int, Action<int>> FreshThreads([CallerMemberName] string test = "") =>
        new AssemblyLoadContext(test).LoadFromAssemblyPath(typeof(LargeStackThreads).Assembly.Location)
            .GetType(typeof(LargeStackThreads).FullName!, throwOnError: true)!
            .GetMethod(nameof(LargeStackThreads.Run))!
            .CreateDelegate<Action<int, Action<int>>>();

    [Fact]
    public void FailuresAreRethrownOnTheCallingThreadLowestIndexFirst()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => LargeStackThreads.Run(3, index =>
        {
            if (index > 0) throw new InvalidOperationException($"failed {index}");
        }));
        Assert.Equal("failed 1", failure.Message);
        Assert.Equal("call failed", Assert.Throws<InvalidOperationException>(() =>
            LargeStackThreads.Call<int>(() => throw new InvalidOperationException("call failed"))).Message);
    }

    [Fact]
    public void NoWorkStartsNoThreadAndBadArgumentsAreRejected()
    {
        LargeStackThreads.Run(0, _ => Assert.Fail("nothing to run"));
        Assert.Throws<ArgumentNullException>(() => LargeStackThreads.Run(1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => LargeStackThreads.Run(-1, _ => { }));
        Assert.Throws<ArgumentNullException>(() => LargeStackThreads.Call<int>(null!));
    }

    // Each frame holds a 1 KB buffer the next call reads, so no frame can be optimized away.
    private static int Recurse(int remaining)
    {
        Span<byte> frame = stackalloc byte[1024];
        frame[remaining % frame.Length] = 1;
        return remaining == 0 ? 0 : 1 + Recurse(remaining - 1) + frame[remaining % frame.Length] - 1;
    }
}
