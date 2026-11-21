// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Loops in catch and finally handlers (issue #259: Sdcb.SimdPaddleOCR's GpuDetGraph.RunCore waits for each fence in a
// finally and ignores a failed wait). A nested catch leaves into its handler's loop, as a catch in the method body
// leaves into the body's loop: the loop keeps one entry, and the leave is a continuation. Each shape compiles on its
// own, so one that fails to compile cannot hide another.
public sealed class HandlerLoopCompilationTests
{
    private const int Count = 6;

    // Step throws for odd values; Count is 6.
    private static readonly IReadOnlyDictionary<string, (string Body, int Result, int Steps)> Shapes =
        new Dictionary<string, (string, int, int)>
        {
            // GpuDetGraph.RunCore's shape.
            ["InFinally"] = ("""
                var caught = 0;
                try { Step(0); }
                finally
                {
                    for (var i = 0; i < count; i++)
                        try { Step(i); } catch (InvalidOperationException) { caught++; }
                }
                return caught;
                """, 3, 7),
            ["InCatch"] = ("""
                var caught = 0;
                try { Step(1); }
                catch (InvalidOperationException)
                {
                    for (var i = 0; i < count; i++)
                        try { Step(i); } catch (InvalidOperationException) { caught++; }
                }
                return caught;
                """, 3, 7),
            ["InFilteredCatch"] = ("""
                var caught = 0;
                try { Step(1); }
                catch (InvalidOperationException) when (count > 0)
                {
                    for (var i = 0; i < count; i++)
                        try { Step(i); } catch (InvalidOperationException) { caught++; }
                }
                return caught;
                """, 3, 7),
            ["FilteredCatchInFinally"] = ("""
                var caught = 0;
                try { Step(0); }
                finally
                {
                    var i = 0;
                    while (i < count)
                    {
                        try { Step(i); }
                        catch (InvalidOperationException) when (i > 1) { caught++; }
                        catch (InvalidOperationException) { caught += 10; }
                        i++;
                    }
                }
                return caught;
                """, 12, 7),
            ["NestedLoopsInFinally"] = ("""
                var caught = 0;
                try { Step(0); }
                finally
                {
                    for (var i = 0; i < count; i++)
                        for (var j = 0; j <= i; j++)
                            try { Step(i + j); } catch (InvalidOperationException) { caught++; }
                }
                return caught;
                """, 9, 22),
            ["InFinallyInCatch"] = ("""
                var caught = 0;
                try { Step(1); }
                catch (InvalidOperationException)
                {
                    try { Step(0); }
                    finally
                    {
                        for (var i = 0; i < count; i++)
                            try { Step(i); } catch (InvalidOperationException) { caught++; }
                    }
                }
                return caught;
                """, 3, 8),
            // finally { foreach (...) try { x.Dispose(); } catch { } }
            ["DisposeAllInFinally"] = ("""
                var resources = new Resource[count];
                for (var i = 0; i < count; i++) resources[i] = new Resource(i);
                var caught = 0;
                try { Step(0); }
                finally
                {
                    foreach (var resource in resources)
                        try { resource.Dispose(); } catch (InvalidOperationException) { caught++; }
                }
                return caught;
                """, 3, 7),
            // The nested catch leaves to the loop's exit and to its continue target.
            ["BreakAndContinueFromNestedCatchInFinally"] = ("""
                var caught = 0;
                var after = 0;
                try { Step(0); }
                finally
                {
                    for (var i = 0; i < count; i++)
                    {
                        try { Step(i); }
                        catch (InvalidOperationException) { caught++; if (caught == 2) break; continue; }
                        after++;
                    }
                }
                return caught * 10 + after;
                """, 22, 5),
            // The continue leaves through a finally in the nested catch.
            ["ContinueThroughFinallyInNestedCatchInCatch"] = ("""
                var caught = 0;
                var cleaned = 0;
                try { Step(1); }
                catch (InvalidOperationException)
                {
                    for (var i = 0; i < count; i++)
                    {
                        try { Step(i); }
                        catch (InvalidOperationException)
                        {
                            try { caught++; continue; }
                            finally { cleaned += 2; }
                        }
                        cleaned += 100;
                    }
                }
                return caught * 1000 + cleaned;
                """, 3306, 7),
            // The nested catch returns from the method, out of both handlers.
            ["ReturnFromNestedCatchInCatch"] = ("""
                var caught = 0;
                try { Step(1); }
                catch (InvalidOperationException)
                {
                    for (var i = 0; i < count; i++)
                        try { Step(i); } catch (InvalidOperationException) { if (i == 3) return caught + 100; caught++; }
                }
                return caught;
                """, 101, 5),
            // A try/finally, not a catch, in the loop's body.
            ["UsingInFinally"] = ("""
                var total = 0;
                try { Step(0); }
                finally
                {
                    for (var i = 0; i < count; i++)
                    {
                        using var resource = new Resource(2 * i);
                        total += i;
                    }
                }
                return total;
                """, 15, 7),
            // The loop starts the handler, so the handler's entry is the loop's header.
            ["DoWhileStartsFinally"] = ("""
                var total = 0;
                try { Step(0); }
                finally
                {
                    do { total += count; count--; } while (count > 0);
                }
                return total;
                """, 21, 1),
            ["CatchingDoWhileStartsFinally"] = ("""
                var caught = 0;
                try { Step(0); }
                finally
                {
                    do
                    {
                        try { Step(count); } catch (InvalidOperationException) { caught++; }
                        count--;
                    }
                    while (count > 0);
                }
                return caught;
                """, 3, 7),
            ["DoWhileStartsCatch"] = ("""
                var total = 0;
                try { Step(1); }
                catch (InvalidOperationException)
                {
                    do { total += count; count--; } while (count > 0);
                }
                return total;
                """, 21, 1),
            // No loop: the code after a nested try/catch belongs to the finally, not to the nested catch.
            ["AfterCatchInFinally"] = ("""
                var caught = 0;
                try { Step(0); }
                finally
                {
                    try { Step(count - 5); } catch (InvalidOperationException) { caught++; }
                    caught += 10;
                }
                return caught;
                """, 11, 2),
        };

    public static TheoryData<string, bool, WasmTarget> Cases()
    {
        var cases = new TheoryData<string, bool, WasmTarget>();
        foreach (var shape in Shapes.Keys)
        {
            foreach (var optimize in new[] { false, true })
            {
                cases.Add(shape, optimize, WasmTarget.Wasm32);
                cases.Add(shape, optimize, WasmTarget.Wasm64);
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ALoopInAHandlerRunsItsBodyAndItsNestedCatches(string shape, bool optimize, WasmTarget target)
    {
        var (body, expectedResult, expectedSteps) = Shapes[shape];
        using var assets = TestAssets.Create();
        var source = $$"""
            using System;
            using System.Runtime.CompilerServices;
            namespace HandlerLoopFixture;
            public sealed class Resource(int id) : IDisposable
            {
                public void Dispose() => EntryPoint.Step(id);
            }
            public static class EntryPoint
            {
                private static int steps;
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static void Step(int value)
                {
                    steps++;
                    if ((value & 1) == 1) throw new InvalidOperationException();
                }
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Shape(int count)
                {
            {{body}}
                }
                public static int Run(int input)
                {
                    steps = 0;
                    var result = Shape(input - {{41 - Count}});
                    if (result != {{expectedResult}}) return 1000 + result;
                    return steps == {{expectedSteps}} ? 42 : 2000 + steps;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("HandlerLoopFixture", source)
            : assets.CompileSource("HandlerLoopFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "HandlerLoopFixture.EntryPoint", "Run", [], Target: target));
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
