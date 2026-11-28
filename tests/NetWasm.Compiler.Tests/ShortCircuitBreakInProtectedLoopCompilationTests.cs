// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A break that shares a block with the loop's natural exit leaves the loop at every level, as on desktop .NET. In a
// loop inside a protected region, Roslyn's Release build sends a short-circuit condition's break to the `leave` the
// loop's own exit uses, so the loop's exit extension is entered from its body as well as from its condition; the body
// must build that code where it leaves rather than fall into the loop's continue. Each shape runs with B() false (the
// first pass breaks) and true (the loop runs to its end); a loop that restarts instead runs Inner until it throws, and
// the method returns -1.
public sealed class ShortCircuitBreakInProtectedLoopCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        namespace ShortCircuitBreak;
        public static class EntryPoint
        {
            static int s_inner;
            static bool s_b;
            static bool s_running;
            static int s_count;
            static void Inner() { if (++s_inner > 100) throw new Exception("infinite loop"); }
            static bool B() => s_b;

            // Each shape is a method of its own holding only the protected region around the loop, as in the report,
            // so the loop's exit `leave` targets a block outside the region; Run calls it and catches the exception a
            // restarted loop ends with.
            static int Run(Func<int> shape, int input)
            {
                s_inner = 0;
                s_b = (input & 1) != 0;
                s_count = 0;
                try { return shape(); }
                catch (Exception) { return -1; }
            }

            // The reported shape: a for loop inside try/finally, breaking on `!B() || s_inner == 0`.
            static int OrShape()
            {
                try
                {
                    for (var pass = 0; pass < 10; ++pass)
                    {
                        Inner();
                        if (!B() || s_inner == 0) break;
                    }
                }
                finally { s_running = false; }
                return s_inner;
            }

            // The same condition written as an else.
            static int AndElseShape()
            {
                try
                {
                    for (var pass = 0; pass < 10; ++pass)
                    {
                        Inner();
                        if (B() && s_inner != 0) { } else break;
                    }
                }
                finally { s_running = false; }
                return s_inner;
            }

            // A catch instead of a finally.
            static int InCatchShape()
            {
                try
                {
                    for (var pass = 0; pass < 10; ++pass)
                    {
                        Inner();
                        if (!B() || s_inner == 0) break;
                    }
                }
                catch (InvalidOperationException) { s_count++; }
                return s_inner * 10 + s_count;
            }

            // A while loop and a do-while loop.
            static int WhileShape()
            {
                try
                {
                    var pass = 0;
                    while (pass < 10)
                    {
                        Inner();
                        if (!B() || s_inner == 0) break;
                        pass++;
                    }
                }
                finally { s_running = false; }
                return s_inner;
            }

            static int DoWhileShape()
            {
                try
                {
                    var pass = 0;
                    do
                    {
                        Inner();
                        if (!B() || s_inner == 0) break;
                    }
                    while (++pass < 10);
                }
                finally { s_running = false; }
                return s_inner;
            }

            // A foreach, whose enumerator has its own finally, inside a try/finally.
            static int ForEachShape()
            {
                var items = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
                var sum = 0;
                try
                {
                    foreach (var item in items)
                    {
                        Inner();
                        sum += item;
                        if (!B() || s_inner == 0) break;
                    }
                }
                finally { s_running = false; }
                return s_inner * 100 + sum;
            }

            // The loop two protected regions deep.
            static int NestedShape()
            {
                try
                {
                    try
                    {
                        for (var pass = 0; pass < 10; ++pass)
                        {
                            Inner();
                            if (!B() || s_inner == 0) break;
                        }
                    }
                    finally { s_count++; }
                }
                finally { s_running = false; }
                return s_inner * 10 + s_count;
            }

            // Three terms, each breaking.
            static int ThreeTermsShape()
            {
                try
                {
                    for (var pass = 0; pass < 10; ++pass)
                    {
                        Inner();
                        if (!B() || s_inner == 0 || s_count > 100) break;
                    }
                }
                finally { s_running = false; }
                return s_inner;
            }

            public static int Or(int input) => Run(OrShape, input);
            public static int AndElse(int input) => Run(AndElseShape, input);
            public static int InCatch(int input) => Run(InCatchShape, input);
            public static int While(int input) => Run(WhileShape, input);
            public static int DoWhile(int input) => Run(DoWhileShape, input);
            public static int ForEach(int input) => Run(ForEachShape, input);
            public static int Nested(int input) => Run(NestedShape, input);
            public static int ThreeTerms(int input) => Run(ThreeTermsShape, input);
        }
        """;

    private static readonly string[] Methods = ["Or", "AndElse", "InCatch", "While", "DoWhile", "ForEach", "Nested", "ThreeTerms"];

    private static readonly int[] Inputs = [0, 1];

    public static TheoryData<FinalWasmOptimization, WasmTarget, bool> Cases()
    {
        var cases = new TheoryData<FinalWasmOptimization, WasmTarget, bool>();
        foreach (var level in new[]
                 {
                     FinalWasmOptimization.None, FinalWasmOptimization.O0, FinalWasmOptimization.O1,
                     FinalWasmOptimization.O3, FinalWasmOptimization.Os, FinalWasmOptimization.Oz,
                 })
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                cases.Add(level, target, true);
                cases.Add(level, target, false);
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachBreakLeavesItsLoopAsOnDesktop(FinalWasmOptimization optimization, WasmTarget target, bool optimizedIl)
    {
        using var assets = TestAssets.Create();
        var assembly = optimizedIl
            ? assets.CompileOptimizedSource("ShortCircuitBreak", Source)
            : assets.CompileSource("ShortCircuitBreak", Source);
        var expected = Expected;

        foreach (var method in Methods)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly, [assets.CoreLib], "ShortCircuitBreak.EntryPoint", method, [], Target: target,
                Optimization: optimization));
            foreach (var input in Inputs)
            {
                Assert.Equal(expected[(method, input)], CompilerTestSupport.ExecuteWithStandardWasiNode(
                    result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
            }
        }
    }

    // What desktop .NET returns: with B() false the first pass breaks (s_inner is 1); with B() true no break holds and
    // all ten passes run. ForEach adds 100 * s_inner to the items' sum; InCatch and Nested add 10 * s_inner to their
    // catch count and inner finally count.
    private static readonly ImmutableDictionary<(string Method, int Input), int> Expected =
        new Dictionary<(string, int), int>
        {
            [("Or", 0)] = 1,
            [("Or", 1)] = 10,
            [("AndElse", 0)] = 1,
            [("AndElse", 1)] = 10,
            [("InCatch", 0)] = 10,
            [("InCatch", 1)] = 100,
            [("While", 0)] = 1,
            [("While", 1)] = 10,
            [("DoWhile", 0)] = 1,
            [("DoWhile", 1)] = 10,
            [("ForEach", 0)] = 101,
            [("ForEach", 1)] = 1055,
            [("Nested", 0)] = 11,
            [("Nested", 1)] = 101,
            [("ThreeTerms", 0)] = 1,
            [("ThreeTerms", 1)] = 10,
        }.ToImmutableDictionary();
}
