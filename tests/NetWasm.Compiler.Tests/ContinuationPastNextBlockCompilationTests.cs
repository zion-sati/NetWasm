// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A protected block with two continuations, one of which jumps over the next protected block to code after it. The
// structurer ended both continuations at the next block, so the jump fell into it: TopLevel(1) ran the second try as
// if it had taken the normal path, and Nested and FromHandler did the same. InLoop keeps the jump inside a loop body.
// Run's checksum is desktop's.
public sealed class ContinuationPastNextBlockCompilationTests
{
    private const int DesktopChecksum = -271220853;

    public static TheoryData<bool, FinalWasmOptimization> Cases()
    {
        var cases = new TheoryData<bool, FinalWasmOptimization>();
        foreach (var optimization in new[]
            { FinalWasmOptimization.None, FinalWasmOptimization.O1, FinalWasmOptimization.O3, FinalWasmOptimization.Oz })
        {
            cases.Add(false, optimization);
            cases.Add(true, optimization);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AJumpOverTheNextProtectedBlockSkipsIt(bool optimize, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            $"ContinuationsPastNextBlock{optimization}{(optimize ? "Release" : "Debug")}",
            Source,
            "ContinuationsPastNextBlock.EntryPoint",
            optimize,
            WasmTarget.Wasm32,
            41,
            [])
        {
            Optimization = optimization,
        });

        Assert.Equal(DesktopChecksum, result);
    }

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace ContinuationsPastNextBlock;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            static int Work(int x) => x == 3 ? throw new InvalidOperationException() : x;

            [MethodImpl(MethodImplOptions.NoInlining)]
            static int Finish(int r) => Work(r % 1000 == 3 ? r + 1 : r) * 3;

            // A continuation of the first try jumps over the second to shared code after it.
            static int TopLevel(int x)
            {
                var r = 0;
                try
                {
                    if (x == 1) goto Early;
                    r += Work(x);
                }
                catch (InvalidOperationException)
                {
                    r = -1;
                }
                r += 10;
                goto Next;
            Early:
                r += Work(x) * 100;
                goto Done;
            Next:
                try { r += Work(x + 1) * 1000; }
                catch (InvalidOperationException) { r -= 5; }
            Done:
                r = Finish(r);
                r = Finish(r + 1);
                return r;
            }

            // The same inside an outer try, where the two trys are nested blocks.
            static int Nested(int x)
            {
                var r = 0;
                try
                {
                    try
                    {
                        if (x == 1) goto Early;
                        r += Work(x);
                    }
                    catch (InvalidOperationException)
                    {
                        r = -1;
                    }
                    r += 10;
                    goto Next;
                Early:
                    r += Work(x) * 100;
                    goto Done;
                Next:
                    try { r += Work(x + 1) * 1000; }
                    catch (InvalidOperationException) { r -= 5; }
                Done:
                    r = Finish(r);
                    r = Finish(r + 1);
                }
                finally
                {
                    r += 1;
                }
                return r;
            }

            // The jump leaves from the catch handler.
            static int FromHandler(int x)
            {
                var r = 0;
                try
                {
                    r += Work(x);
                }
                catch (InvalidOperationException)
                {
                    r = -1;
                    if (x == 3) goto Early;
                }
                r += 10;
                goto Next;
            Early:
                r += Work(x + 1) * 100;
                goto Done;
            Next:
                try { r += Work(x + 1) * 1000; }
                catch (InvalidOperationException) { r -= 5; }
            Done:
                r = Finish(r);
                r = Finish(r + 1);
                return r;
            }

            // Inside a loop body, the jump over the second try stays in the loop.
            static int InLoop(int x)
            {
                var r = 0;
                for (var i = 0; i < 3; i++)
                {
                    try
                    {
                        if (x == i) goto Early;
                        r += Work(x);
                    }
                    catch (InvalidOperationException)
                    {
                        r = -1;
                    }
                    r += 10;
                    goto Next;
                Early:
                    r += Work(x) * 100;
                    goto Done;
                Next:
                    try { r += Work(x + i) * 1000; }
                    catch (InvalidOperationException) { r -= 5; }
                Done:
                    r = Finish(r) % 100000;
                }
                return r;
            }

            public static int Run(int input)
            {
                var total = input;
                for (var x = 0; x < 4; x++)
                {
                    total = unchecked(total * 31 + TopLevel(x));
                    total = unchecked(total * 31 + Nested(x));
                    total = unchecked(total * 31 + FromHandler(x));
                    total = unchecked(total * 31 + InLoop(x));
                }
                return total;
            }
        }
        """;
}
