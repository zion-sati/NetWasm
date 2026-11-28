// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A point in a protected block that can collect only while what it throws travels to a handler here (a field load, an
// array length, a call of a method that cannot allocate, a throw) publishes what the handler reads, and leaves
// unpublished, while its slot holds null, what only its normal path reads. So a root a catch or a finally reads must stay
// held through the collection its exception's allocation makes, and a root left unpublished at such a point must be
// published at the next point that collects, plainly and in a runtime-async method, whose body is one protected block.
// Each collection is followed by garbage that reuses freed memory, so a holder collected too early reads back wrong.
public sealed class ThrowOnlyRootGcTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        namespace ThrowOnlyRootFixture;

        public sealed class Holder
        {
            public int Value;

            public Holder(int value) => Value = value;
        }

        public static class EntryPoint
        {
            private static Holder? s_missing;
            private static int s_zero;
            private static Exception? s_failure;

            // False at run time; each method's early return publishes nothing, so its root frame starts zeroed.
            private static bool s_fast;
            private static Holder? s_sink;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Collect()
            {
                GC.Collect();
                GC.Collect();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Churn()
            {
                for (var index = 0; index < 64; index++) s_sink = new Holder(-1 - index);
            }

            // Allocates nothing; the holder escapes into it, so no optimization removes its allocation.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Escape(Holder holder)
            {
                if (holder.Value < 0) s_sink = holder;
            }

            // Allocates nothing; throws DivideByZeroException when the divisor is 0.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Divide(int left, int right) => left / right;

            // A root the catch reads, held across a field load that throws.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CatchAfterFieldLoad(int value)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                try
                {
                    Escape(keep);
                    return s_missing!.Value + keep.Value;
                }
                catch (NullReferenceException)
                {
                    Collect();
                    Churn();
                    return keep.Value;
                }
            }

            // A root a finally reads while the exception of a call that cannot allocate passes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FinallyAfterDivide(int value)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                var result = -1;
                try
                {
                    try
                    {
                        Escape(keep);
                        result = Divide(keep.Value, s_zero);
                    }
                    finally
                    {
                        Collect();
                        Churn();
                        result = keep.Value;
                    }
                }
                catch (DivideByZeroException)
                {
                }
                return result;
            }

            // A root and the exception the catch reads, across a throw.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CatchAfterThrow(int value)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                try
                {
                    Escape(keep);
                    if (value >= 0) throw new InvalidOperationException("thrown");
                    return -1;
                }
                catch (InvalidOperationException exception)
                {
                    Collect();
                    Churn();
                    return exception.Message == "thrown" ? keep.Value : -2;
                }
            }

            // A root only the normal path reads: unpublished at the array length's check, then published at the call
            // that collects.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NormalPathAfterCheck(int value, int[] values)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                try
                {
                    var length = values.Length;
                    Collect();
                    Churn();
                    return keep.Value + length - values.Length;
                }
                catch (NullReferenceException)
                {
                    return -1;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Holder Pass(Holder holder, int length, int counted) =>
                holder.Value >= 0 && length == 3 && counted == 7 ? holder : throw new ArgumentException("lost");

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CollectAndCount()
            {
                Collect();
                Churn();
                return 7;
            }

            // A holder on the evaluation stack: unpublished at the array length's check, which collects only by
            // throwing, then published at the call that collects while it is still there.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StackValueAfterCheck(int value, int[] values)
            {
                if (s_fast) return value;
                try
                {
                    return Pass(new Holder(value), values.Length, CollectAndCount()).Value;
                }
                catch (NullReferenceException)
                {
                    return -1;
                }
            }

            // The same in a runtime-async method.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static async Task<int> AsyncStackValueAfterCheck(int value, int[] values)
            {
                if (s_fast) return value;
                return Pass(new Holder(value), values.Length, CollectAndCount()).Value;
            }

            // A root only the normal path reads, in a runtime-async method.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static async Task<int> AsyncNormalPath(int value, int[] values)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                var length = values.Length;
                Collect();
                Churn();
                return keep.Value + length - values.Length;
            }

            // Where the check throws, the async method's task holds the exception.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static async Task<int> AsyncThrows(int value, int[]? values)
            {
                var keep = new Holder(value);
                var length = values!.Length;
                Collect();
                return keep.Value + length;
            }

            public static int Run(int input)
            {
                s_fast = input > 1000;
                s_missing = input > 1000 ? new Holder(input) : null;
                s_zero = input > 1000 ? 1 : 0;
                s_failure = new InvalidOperationException("unused");
                if (CatchAfterFieldLoad(input) != input) return 10;
                if (FinallyAfterDivide(input) != input) return 11;
                if (CatchAfterThrow(input) != input) return 12;
                if (NormalPathAfterCheck(input, new int[3]) != input) return 13;
                if (AsyncNormalPath(input, new int[3]).GetAwaiter().GetResult() != input) return 14;
                if (StackValueAfterCheck(input, new int[3]) != input) return 17;
                if (AsyncStackValueAfterCheck(input, new int[3]).GetAwaiter().GetResult() != input) return 18;
                var faulted = AsyncThrows(input, null);
                Collect();
                Churn();
                if (!faulted.IsFaulted || faulted.Exception!.InnerException is not NullReferenceException) return 15;
                return s_failure.Message == "unused" ? input + 1 : 16;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.O3)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.Oz)]
    [InlineData("Debug-Wasm64-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm64-Direct-Linked", FinalWasmOptimization.O3)]
    public void AThrowOnlyPointHoldsWhatHandlersReadAndLaterPublishesTheRest(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("ThrowOnlyRootFixture", "ThrowOnlyRootFixture", Source, [41])
        {
            CaseId = "frame.throw-only-roots",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare roots held across points that collect only by throwing, in handlers and on the normal path, against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
