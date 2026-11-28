// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A catch without filters registers its exception frame only while a frame with filters is registered; otherwise its
// landing pad selects its own clause. Against desktop .NET: clauses taken in order or passed on with no filter live; a
// catch inside a filter's protected block stops the first pass before the filter; a filter inside a catch's protected
// block runs first, before the finally blocks between; catches entered in a loop with a filter live on some passes,
// and a rethrow; and a runtime-async catch resumed while a filter's protected block is live, which must register again
// so the filter never sees what the catch takes. Each handler allocates, so the landing pads' rewinds meet collections.
public sealed class FilterGuardedCatchDifferentialTests(CorrectnessTestRunner runner)
{
    public static TheoryData<FinalWasmOptimization, string> Cases()
    {
        var cases = new TheoryData<FinalWasmOptimization, string>();
        foreach (var optimization in (FinalWasmOptimization[])[FinalWasmOptimization.None, FinalWasmOptimization.O1,
                     FinalWasmOptimization.O3, FinalWasmOptimization.Oz])
        {
            cases.Add(optimization, "Release-Wasm32-Direct-Linked");
            cases.Add(optimization, "Debug-Wasm32-Direct-Linked");
        }
        // The catch types' addresses and the selection's arguments are 64-bit there.
        cases.Add(FinalWasmOptimization.None, "Release-Wasm64-Direct-Linked");
        cases.Add(FinalWasmOptimization.O3, "Release-Wasm64-Direct-Linked");
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CatchesWithoutFiltersMatchDesktop(FinalWasmOptimization optimization, string cell) =>
        runner.Run(new CorpusFixture(
            "FilterGuardedCatches", "NetWasm.Correctness.FilterGuardedCatches", Source, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            CaseId = "exceptions.filter-guarded-catches",
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare NetWasm's catch selection and filter order with desktop .NET's.",
            Optimization = optimization,
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        namespace NetWasm.Correctness.FilterGuardedCatches;

        public sealed class Later : INotifyCompletion
        {
            public bool IsCompleted => false;
            public void OnCompleted(Action continuation) => EntryPoint.Pending.Enqueue(continuation);
            public void GetResult() { }
            public Later GetAwaiter() => this;
        }

        public static class EntryPoint
        {
            internal static readonly Queue<Action> Pending = new();
            private static readonly List<string> Kept = new();
            private static int _trace;

            private static void Mark(int value)
            {
                Kept.Add(new string('x', value % 7 + 1));
                _trace = unchecked(_trace * 31 + value);
            }

            private static bool Log(int value, bool result)
            {
                Mark(value);
                return result;
            }

            private static Exception Make(int kind) => (kind % 5) switch
            {
                0 => new IOException("io"),
                1 => new ArgumentNullException("name"),
                2 => new ArgumentException("argument"),
                3 => new InvalidOperationException("operation"),
                _ => new FormatException("format"),
            };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Throw(int kind)
            {
                var garbage = new object[kind % 3 + 1];
                garbage[0] = new string('y', kind + 1);
                throw Make(kind);
            }

            private static void Clauses(int kind)
            {
                try
                {
                    try
                    {
                        try { Throw(kind); }
                        catch (ArgumentNullException) { Mark(11); }
                        catch (ArgumentException) { Mark(12); }
                        catch (IOException) { Mark(13); }
                    }
                    finally { Mark(14); }
                }
                catch (InvalidOperationException) { Mark(15); }
                catch (Exception) { Mark(16); }
            }

            private static void InsideFilter(int kind)
            {
                try
                {
                    try { Throw(kind); }
                    catch (IOException) { Mark(21); }
                    catch (ArgumentException) { Mark(22); }
                }
                catch (Exception) when (Log(23, kind % 5 != 4)) { Mark(24); }
                catch (Exception) { Mark(25); }
            }

            private static void FilterInside(int kind)
            {
                try
                {
                    try
                    {
                        try { Throw(kind); }
                        finally { Mark(31); }
                    }
                    catch (Exception) when (Log(32, kind % 5 == 2)) { Mark(33); }
                }
                catch (IOException) { Mark(34); }
                catch (Exception) { Mark(35); }
            }

            private static void Loop(int kind)
            {
                for (var i = 0; i < 3; i++)
                {
                    try
                    {
                        if (i == 1)
                        {
                            try { Throw(kind); }
                            catch (Exception) when (Log(41, (kind & 1) == 0)) { Mark(42); }
                        }
                        try { Throw(kind + i); }
                        catch (IOException) { Mark(43); throw; }
                        catch (Exception) { Mark(44); }
                    }
                    catch (Exception) when (Log(45, i != 2)) { Mark(46); }
                    catch (IOException) { Mark(47); }
                }
            }

            private static async Task Resumed(int kind)
            {
                try
                {
                    await new Later();
                    Throw(kind);
                }
                catch (IOException) { Mark(51); }
                catch (ArgumentException) { Mark(52); }
            }

            public static int Trace() => _trace;

            public static int Run(int input)
            {
                _trace = 17;
                Pending.Clear();
                Kept.Clear();
                Clauses(input);
                InsideFilter(input);
                FilterInside(input);
                try { Loop(input); }
                catch (Exception) { Mark(48); }
                var task = Resumed(input);
                try
                {
                    while (Pending.Count != 0) Pending.Dequeue()();
                }
                catch (Exception) when (Log(53, true)) { Mark(54); }
                Mark(task.IsFaulted ? 55 : task.IsCompleted ? 56 : 57);
                return unchecked(_trace * 31 + Kept.Count);
            }
        }
        """;
}
