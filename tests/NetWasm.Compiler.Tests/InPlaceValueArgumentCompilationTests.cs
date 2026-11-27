// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A call reads an unaddressed local or argument in place, in a method with catch, finally or fault handlers too: a
// callee that writes its parameter copies it first, so the caller's local keeps its value, inside a protected block and
// in a handler. A method with a filter keeps its copies: the filter runs while the call that threw is still active, and
// one that assigns the local must not change what the callee's finally reads through its parameter.
public sealed class InPlaceValueArgumentCompilationTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace InPlaceValueArgumentFixture;

        public struct Big
        {
            public long A;
            public long B;
            public long C;
            public long D;
        }

        public static class EntryPoint
        {
            private static long s_seen;

            // Writes its parameter: its own copy, never the caller's local.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Writes(Big big)
            {
                big.A += 100;
                big.D = big.A;
                return big.A + big.B + big.D;
            }

            // Reads its parameter in its finally, after the caller's filter has run.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ThrowsThenReads(Big big)
            {
                try
                {
                    throw new InvalidOperationException("x");
                }
                finally
                {
                    s_seen = big.A + big.C;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long InProtectedBlocks(Big big)
            {
                long total = 0;
                try
                {
                    total += Writes(big);
                    if (big.A != 1 || big.D != 4) return -10;
                    throw new InvalidOperationException("y");
                }
                catch (InvalidOperationException)
                {
                    total += Writes(big);
                    if (big.A != 1 || big.D != 4) return -11;
                }
                finally
                {
                    total += Writes(big);
                }
                return big.A == 1 && big.D == 4 ? total : -12;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long WithFilter(Big big, Big changed)
            {
                try
                {
                    ThrowsThenReads(big);
                }
                catch (InvalidOperationException) when ((big = changed).A == changed.A)
                {
                }
                return s_seen;
            }

            public static int Run(int input)
            {
                var big = new Big { A = 1, B = 2, C = 3, D = 4 };
                var total = InProtectedBlocks(big);
                if (total != 3 * (101 + 2 + 101)) return 10;
                if (big.A != 1 || big.D != 4) return 11;
                var seen = WithFilter(big, new Big { A = 70, B = 0, C = 7, D = 0 });
                if (seen != 1 + 3) return 12;
                return input + 1;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.O3)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.Oz)]
    [InlineData("Release-Wasm64-Direct-Linked", FinalWasmOptimization.O3)]
    public void ValueArgumentsReadInPlaceKeepTheirCallersValues(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("InPlaceValueArgumentFixture", "InPlaceValueArgumentFixture", Source, [41])
        {
            CaseId = "frame.in-place-value-argument",
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare value arguments read in place in methods with handlers, and a filter that assigns the local passed, against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
