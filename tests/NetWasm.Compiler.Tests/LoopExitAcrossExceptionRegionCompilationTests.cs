// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Loops without a condition of their own (while (true), for (;;)) whose only exits leave a
// protected region: a break, continue or return inside lock, try/finally or try/catch.
// Upstream AsyncStreamReader.FlushMessageQueue has the lock shape, and the PEM and key
// import helpers have the return and continue shapes. Each case checks the result and how
// often the cleanup ran, so a protected block that runs outside its region fails.
public sealed class LoopExitAcrossExceptionRegionCompilationTests
{
    public static TheoryData<string, bool, WasmTarget> Cases()
    {
        var cases = new TheoryData<string, bool, WasmTarget>();
        foreach (var shape in Shapes.Keys)
        {
            foreach (var optimize in new[] { false, true })
            {
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                {
                    cases.Add(shape, optimize, target);
                }
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LoopsLeftOnlyThroughProtectedRegionsCompileAndRun(string shape, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"LoopExit{shape}{(optimize ? "Release" : "Debug")}{target}",
            Prefix + Shapes[shape] + Suffix,
            "LoopExit.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, observed);
    }

    private const string Prefix = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        namespace LoopExit;
        public static class EntryPoint
        {
            static readonly object Gate = new();
            static Queue<int> Items() => new(new[] { 1, 2, 3 });

        """;

    private const string Suffix = """

        }
        """;

    private static readonly Dictionary<string, string> Shapes = new()
    {
        ["BreakOutOfLock"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    while (true)
                    {
                        lock (Gate)
                        {
                            if (queue.Count == 0)
                            {
                                break;
                            }
                            sum += queue.Dequeue();
                        }
                    }
                    return Monitor.IsEntered(Gate) ? 1 : input - 5 + sum;
                }
            """,
        ["BreakOutOfFinally"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    var cleanups = 0;
                    while (true)
                    {
                        try
                        {
                            if (queue.Count == 0)
                            {
                                break;
                            }
                            sum += queue.Dequeue();
                        }
                        finally
                        {
                            cleanups++;
                        }
                    }
                    return input - 9 + sum + cleanups;
                }
            """,
        ["BreakOutOfLockInsideTryCatch"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    try
                    {
                        while (true)
                        {
                            lock (Gate)
                            {
                                if (queue.Count == 0)
                                {
                                    break;
                                }
                                sum += queue.Dequeue();
                            }
                        }
                        return Monitor.IsEntered(Gate) ? 1 : input - 5 + sum;
                    }
                    catch (InvalidOperationException)
                    {
                        return 2;
                    }
                }
            """,
        ["BreakOutOfFinallyInsideFinally"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    var cleanups = 0;
                    try
                    {
                        while (true)
                        {
                            try
                            {
                                if (queue.Count == 0)
                                {
                                    break;
                                }
                                sum += queue.Dequeue();
                            }
                            finally
                            {
                                cleanups++;
                            }
                        }
                    }
                    finally
                    {
                        cleanups += 10;
                    }
                    return input - 19 + sum + cleanups;
                }
            """,
        ["BreakOutOfTryCatch"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    var caught = 0;
                    while (true)
                    {
                        try
                        {
                            if (queue.Count == 0)
                            {
                                break;
                            }
                            var item = queue.Dequeue();
                            if (item == 2)
                            {
                                throw new InvalidOperationException();
                            }
                            sum += item;
                        }
                        catch (InvalidOperationException)
                        {
                            caught++;
                        }
                    }
                    return input - 2 + sum - caught;
                }
            """,
        ["ReturnOutOfFinally"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    var cleanups = 0;
                    var result = Drain(queue, ref sum, ref cleanups);
                    return result == 6 ? input - 9 + sum + cleanups : 1;
                }

                static int Drain(Queue<int> queue, ref int sum, ref int cleanups)
                {
                    for (;;)
                    {
                        try
                        {
                            if (queue.Count == 0)
                            {
                                return sum;
                            }
                            sum += queue.Dequeue();
                        }
                        finally
                        {
                            cleanups++;
                        }
                    }
                }
            """,
        ["BreakAndContinueOutOfFinally"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    var cleanups = 0;
                    while (true)
                    {
                        try
                        {
                            if (queue.Count == 0)
                            {
                                break;
                            }
                            var item = queue.Dequeue();
                            if (item == 2)
                            {
                                continue;
                            }
                            sum += item;
                        }
                        finally
                        {
                            cleanups++;
                        }
                    }
                    return input - 7 + sum + cleanups;
                }
            """,
        ["BreakOutOfFinallyInsideLock"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    var cleanups = 0;
                    while (true)
                    {
                        lock (Gate)
                        {
                            try
                            {
                                if (queue.Count == 0)
                                {
                                    break;
                                }
                                sum += queue.Dequeue();
                            }
                            finally
                            {
                                cleanups++;
                            }
                        }
                    }
                    return Monitor.IsEntered(Gate) ? 1 : input - 9 + sum + cleanups;
                }
            """,
        ["GotoOutOfNestedLoopsThroughFinally"] = """
                public static int Run(int input)
                {
                    var sum = 0;
                    var cleanups = 0;
                    for (var outer = 0; outer < 3; outer++)
                    {
                        var queue = Items();
                        while (true)
                        {
                            try
                            {
                                if (queue.Count == 0)
                                {
                                    break;
                                }
                                var item = queue.Dequeue();
                                if (outer == 1 && item == 2)
                                {
                                    goto Done;
                                }
                                sum += item;
                            }
                            finally
                            {
                                cleanups++;
                            }
                        }
                    }
                Done:
                    return input - 12 + sum + cleanups;
                }
            """,
        ["ReturnOutOfFinallyIndexed"] = """
                static int visited;

                public static int Run(int input) =>
                    FirstAtLeast(new[] { 1, 5, 42, 50 }, input) == 42 && visited == 3 ? input + 1 : 1;

                static int FirstAtLeast(int[] values, int limit)
                {
                    var i = 0;
                    while (true)
                    {
                        try
                        {
                            if (values[i] >= limit)
                            {
                                return values[i];
                            }
                        }
                        finally
                        {
                            i++;
                            visited = i;
                        }
                    }
                }
            """,
        ["ContinueAndReturnOutOfFinally"] = """
                public static int Run(int input)
                {
                    var values = new[] { 1, 5, 42, 50 };
                    return FirstAtLeast(values, input) == 42 && values[0] == 49 ? input + 1 : 1;
                }

                static int FirstAtLeast(int[] values, int limit)
                {
                    var i = 0;
                    while (true)
                    {
                        var seen = 0;
                        try
                        {
                            seen = values[i];
                            if (seen < limit)
                            {
                                i++;
                                continue;
                            }
                            return seen;
                        }
                        finally
                        {
                            values[0] += seen;
                        }
                    }
                }
            """,
        ["ContinueOutOfFinallyThenReturn"] = """
                public static int Run(int input)
                {
                    var values = new[] { 1, 5, 42, 50 };
                    return FirstAtLeast(values, input) == 42 && values[0] == 49 ? input + 1 : 1;
                }

                static int FirstAtLeast(int[] values, int limit)
                {
                    var i = 0;
                    while (true)
                    {
                        var seen = 0;
                        try
                        {
                            seen = values[i];
                            if (seen < limit)
                            {
                                i++;
                                continue;
                            }
                        }
                        finally
                        {
                            values[0] += seen;
                        }
                        return seen;
                    }
                }
            """,
        ["FlushMessageQueueShape"] = """
                static readonly Queue<string> messages = new(new[] { "a", "bb", "ccc" });
                static bool cancelled;
                static int received;

                public static int Run(int input) => !Flush(true) && received == 6 ? input + 1 : 1;

                static bool Flush(bool rethrowLater)
                {
                    try
                    {
                        while (true)
                        {
                            string line;
                            lock (messages)
                            {
                                if (messages.Count == 0)
                                {
                                    break;
                                }
                                line = messages.Dequeue();
                            }

                            if (!cancelled)
                            {
                                received += line.Length;
                            }
                        }
                        return false;
                    }
                    catch (Exception)
                    {
                        if (rethrowLater)
                        {
                            return true;
                        }
                        throw;
                    }
                }
            """,
        ["DoWhileTrueBreakOutOfFinally"] = """
                public static int Run(int input)
                {
                    var queue = Items();
                    var sum = 0;
                    var cleanups = 0;
                    do
                    {
                        try
                        {
                            if (queue.Count == 0)
                            {
                                break;
                            }
                            sum += queue.Dequeue();
                        }
                        finally
                        {
                            cleanups++;
                        }
                    }
                    while (true);
                    return input - 9 + sum + cleanups;
                }
            """,
    };
}
