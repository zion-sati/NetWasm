// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Without [SkipLocalsInit], stackalloc blocks and locals start zeroed every time a method is entered (ECMA-335
// III.3.47 localloc, II.24.4.4 .locals init), as desktop .NET runs them: after the stack held other values, in a loop,
// in a callee inlined into a loop, and in one inlined into a caller that skips its own initialization. A callee that
// reads a local before writing it (Unsafe.SkipInit) must read zero on each call.
public sealed class LocalsInitCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void LocalsAndStackallocStartZeroedAtEveryEntry() =>
        runner.Run(new("LocalsInit", "LocalsInit", Source, [0, 1, 4, 9])
        {
            AllowUnsafe = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace LocalsInit;

        public static class EntryPoint
        {
            private static int _trace;
            private static int _sink;

            public static int Trace() => _trace;

            // Leaves non-zero values where the next frames' blocks and locals will be.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Dirty(int seed)
            {
                Span<int> block = stackalloc int[64];
                for (var i = 0; i < block.Length; i++) block[i] = 0x5A5A5A5A + i + seed;
                _sink += block[seed & 63];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumFresh()
            {
                Span<int> block = stackalloc int[4];
                return block[0] | block[1] | block[2] | block[3];
            }

            // Small enough to inline: its block must be zero at each inlined entry too.
            private static int Fresh(int seed)
            {
                Span<int> block = stackalloc int[4];
                var before = block[0] | block[1] | block[2] | block[3];
                block[seed & 3] = seed;
                _sink += block[seed & 3];
                return before;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Sized(int count)
            {
                Span<int> block = stackalloc int[count];
                var or = 0;
                foreach (var value in block) or |= value;
                block.Fill(count);
                _sink += block[0];
                return or;
            }

            // Reads its local before writing it: .locals init makes it zero at every entry, inlined or not.
            private static int Unwritten(int write)
            {
                Unsafe.SkipInit(out int value);
                var before = value;
                value = write;
                Keep(ref value);
                return before;
            }

            // A struct whose second field the callee never writes: zero at every entry.
            private struct Pair
            {
                public int First;
                public int Second;
            }

            private static int HalfWritten(int write)
            {
                Unsafe.SkipInit(out Pair pair);
                var before = pair.Second;
                pair.First = write;
                pair.Second = write + 1;
                KeepPair(ref pair);
                return before;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Keep(ref int value) => value++;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void KeepPair(ref Pair pair) => _sink += pair.First + pair.Second;

            [SkipLocalsInit]
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SkippingCaller(int input)
            {
                var or = 0;
                for (var i = 0; i < 3; i++) or |= Fresh(i + input + 1) | Unwritten(i + 7) | HalfWritten(i + 3);
                return or;
            }

            public static int Run(int input)
            {
                var hash = 17;
                void Mix(int value) => hash = unchecked(hash * 31 + value);

                Dirty(input);
                Mix(SumFresh());
                var loop = 0;
                for (var i = 0; i < 4; i++)
                {
                    Span<int> block = stackalloc int[4];
                    loop |= block[0] | block[1] | block[2] | block[3];
                    block[i] = 0x1111 * (i + 1);
                    _sink += block[i];
                }
                Mix(loop);
                Dirty(input + 1);
                var inlined = 0;
                for (var i = 1; i < 5; i++) inlined |= Fresh(i + input);
                Mix(inlined);
                Dirty(input + 2);
                Mix(Sized(5 + input));
                Mix(Sized(13));
                var unwritten = 0;
                for (var i = 1; i < 5; i++) unwritten |= Unwritten(i * 3 + input) | HalfWritten(i);
                Mix(unwritten);
                Dirty(input + 3);
                Mix(SkippingCaller(input));
                _trace = hash;
                return hash;
            }
        }
        """;
}
