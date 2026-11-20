// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// stackalloc in a method with localsinit (C#'s default) returns zeroed memory, as ECMA-335 III.3.47 requires, even
// where an earlier frame left other bytes: constant and computed sizes, struct elements, and an allocation repeated
// in a loop. It runs on the native runtime, whose value frames reuse the memory an earlier frame left, and on desktop.
public sealed class LocalAllocationCompilationTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace LocalAllocationFixture;

        public struct Pair { public long A; public int B; }

        public static unsafe class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Pollute(int bytes)
            {
                byte* block = stackalloc byte[bytes];
                new Span<byte>(block, bytes).Fill(0xFF);
                return block[bytes - 1];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long SumConstant()
            {
                Span<uint> words = stackalloc uint[16];
                long sum = 0;
                foreach (var word in words) sum += word;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long SumComputed(int count)
            {
                int* values = stackalloc int[count];
                long sum = 0;
                for (var i = 0; i < count; i++) sum += values[i];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long SumPairs(int count)
            {
                Span<Pair> pairs = stackalloc Pair[count];
                long sum = 0;
                foreach (var pair in pairs) sum += pair.A + pair.B;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long SumInLoop(int rounds)
            {
                long sum = 0;
                for (var round = 0; round < rounds; round++)
                {
                    #pragma warning disable CA2014
                    byte* block = stackalloc byte[24];
                    #pragma warning restore CA2014
                    for (var i = 0; i < 24; i++) sum += block[i];
                    block[round % 24] = 0x7F;
                }
                return sum;
            }

            public static int Run(int input)
            {
                if (Pollute(8192) != 0xFF) return 1;
                if (SumConstant() != 0) return 2;
                Pollute(8192);
                if (SumComputed(input * 50) != 0) return 3;
                Pollute(8192);
                if (SumPairs(input) != 0) return 4;
                Pollute(8192);
                if (SumInLoop(input) != 0) return 5;
                Pollute(8192);
                if (SumComputed(1) != 0) return 6;
                Pollute(8192);
                if (SumComputed(4000) != 0) return 7;
                return 42;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Debug-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void StackallocUnderLocalsInitIsZeroed(string cell)
    {
        var fixture = new CorpusFixture("LocalAllocationFixture", "LocalAllocationFixture", Source, [41])
        {
            CaseId = "stackalloc.localsinit-zeroed",
            ExpectedReturnValue = 42,
            AllowUnsafe = true,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare stackalloc's zeroed memory under localsinit against the desktop runtime.",
            Optimization = FinalWasmOptimization.None,
        };
        runner.Run(fixture);
    }
}
