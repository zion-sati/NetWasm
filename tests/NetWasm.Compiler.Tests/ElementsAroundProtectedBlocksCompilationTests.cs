// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// An array access proves the same element's checks in the blocks its block dominates, in a method with protected blocks
// too, but not in a handler, which control enters from the access itself when the index is out of range; a handler whose
// loop branches back to its first block is no exception. Proven there, the finally's store took what lies past the array
// where desktop throws first. Run's checksum is desktop's.
public sealed class ElementsAroundProtectedBlocksCompilationTests
{
    private const int DesktopChecksum = -592020920;

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
    public void AHandlerChecksTheElementItsProtectedBlockAccessed(bool optimize, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            $"ElementsAroundProtectedBlocks{optimization}{(optimize ? "Release" : "Debug")}",
            Source,
            "ElementsAroundProtectedBlocks.EntryPoint",
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
        namespace ElementsAroundProtectedBlocks;

        public static class EntryPoint
        {
            static int s_sink;

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void Work(int value)
            {
                if (value == 12345) throw new InvalidOperationException();
            }

            // The try's access checks values[index] before anything else, so the blocks it dominates may skip both checks; the
            // finally may not: control reaches it from that access itself when index is out of range, and its loop branches back
            // to its first block.
            [MethodImpl(MethodImplOptions.NoInlining)]
            static void Read(int[] values, int index, int bound)
            {
                try
                {
                    s_sink = values[index];
                    Work(s_sink);
                    if (bound > 3) s_sink += values[index] * 2;
                }
                finally
                {
                    do { s_sink = values[index] + bound; bound--; } while (bound > 0);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static int Probe(int[] values, int index, int bound)
            {
                s_sink = -7;
                try
                {
                    Read(values, index, bound);
                    return s_sink;
                }
                catch (IndexOutOfRangeException)
                {
                    return s_sink * 1000;
                }
            }

            public static int Run(int input)
            {
                var values = new[] { 3, 5, 8, 13 };
                var total = 0;
                foreach (var index in new[] { 1, 3, 4, input })
                {
                    foreach (var bound in new[] { 1, 2, 5 })
                    {
                        total = unchecked(total * 31 + Probe(values, index, bound));
                    }
                }
                return total;
            }
        }
        """;
}
