// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A dereference proves a managed address non-null in the blocks its block dominates, in a method with protected blocks
// too, but not in a handler, which control enters from the dereference itself when the address is null; a handler whose
// loop branches back to its first block is no exception. Proven there, the finally's store took what lies at address 0
// where desktop throws first. Run's checksum is desktop's.
public sealed class DereferencesAroundProtectedBlocksCompilationTests
{
    private const int DesktopChecksum = 1235512446;

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
    public void AHandlerChecksWhatItsProtectedBlockDereferenced(bool optimize, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            $"DereferencesAroundProtectedBlocks{optimization}{(optimize ? "Release" : "Debug")}",
            Source,
            "DereferencesAroundProtectedBlocks.EntryPoint",
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
        namespace DereferencesAroundProtectedBlocks;

        public static class EntryPoint
        {
            static int s_sink;

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void Work(int value)
            {
                if (value == 12345) throw new InvalidOperationException();
            }

            // The try dereferences value before anything else, so the blocks after it may skip the check; the finally may not:
            // control reaches it from the dereference itself when value is null, and its loop branches back to its first block.
            [MethodImpl(MethodImplOptions.NoInlining)]
            static void Store(ref int value, int bound)
            {
                try
                {
                    s_sink = value;
                    Work(s_sink);
                    if (bound > 3) s_sink += value * 2;
                }
                finally
                {
                    do { s_sink = value + bound; bound--; } while (bound > 0);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static int Probe(bool missing, int start, int bound)
            {
                var cell = start;
                s_sink = -7;
                try
                {
                    if (missing) Store(ref Unsafe.NullRef<int>(), bound);
                    else Store(ref cell, bound);
                    return s_sink;
                }
                catch (NullReferenceException)
                {
                    return s_sink * 1000;
                }
            }

            public static int Run(int input)
            {
                var total = 0;
                foreach (var missing in new[] { false, true })
                {
                    foreach (var bound in new[] { 1, 2, 5 })
                    {
                        total = unchecked(total * 31 + Probe(missing, input, bound));
                    }
                }
                return total;
            }
        }
        """;
}
