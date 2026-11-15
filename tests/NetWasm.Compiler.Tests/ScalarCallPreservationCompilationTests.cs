// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ScalarCallPreservationCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, ManagedScalarCallPreservation.Enabled)]
    [InlineData(WasmTarget.Wasm64, ManagedScalarCallPreservation.Enabled)]
    [InlineData(WasmTarget.Wasm32, ManagedScalarCallPreservation.Disabled)]
    [InlineData(WasmTarget.Wasm64, ManagedScalarCallPreservation.Disabled)]
    public void PreservesScalarBitsAndExceptionalReturn(
        WasmTarget target, ManagedScalarCallPreservation preservation)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ScalarCallPreservationFixture", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new(ScalarCallPreservation: preservation)));

        Assert.Equal(0, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private static int s_calls;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Touch(bool fail)
            {
                s_calls++;
                if (fail) throw new InvalidOperationException();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Single(float initial, float replacement, int count, bool fail)
            {
                var value = initial;
                for (var i = 0; i < count; i++) value = replacement;
                Touch(fail);
                return value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double Double(double initial, double replacement, int count, bool fail)
            {
                var value = initial;
                for (var i = 0; i < count; i++) value = replacement;
                Touch(fail);
                return value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Integer(long initial, long replacement, int count, bool fail)
            {
                var value = initial;
                for (var i = 0; i < count; i++) value = replacement;
                Touch(fail);
                return value;
            }

            public static int Run(int input)
            {
                int[] singles = [0, int.MinValue, 1, 0x7f800000, unchecked((int)0xff800000), 0x7fc12345, 0x3fc00000];
                long[] doubles = [0, long.MinValue, 1, 0x7ff0000000000000, unchecked((long)0xfff0000000000000), 0x7ff8123456789abc, 0x3ff8000000000000];
                for (var i = 0; i < singles.Length; i++)
                {
                    var single = BitConverter.Int32BitsToSingle(singles[i]);
                    var number = BitConverter.Int64BitsToDouble(doubles[i]);
                    if (BitConverter.SingleToInt32Bits(Single(single, 3, 0, false)) != singles[i]) return 1;
                    if (BitConverter.SingleToInt32Bits(Single(3, single, 3, false)) != singles[i]) return 2;
                    if (BitConverter.DoubleToInt64Bits(Double(number, 3, 0, false)) != doubles[i]) return 3;
                    if (BitConverter.DoubleToInt64Bits(Double(3, number, 3, false)) != doubles[i]) return 4;
                }
                long[] integers = [long.MinValue, long.MaxValue, 0, -1, 1];
                for (var i = 0; i < integers.Length; i++)
                {
                    if (Integer(integers[i], 3, 0, false) != integers[i]) return 7;
                    if (Integer(3, integers[i], 3, false) != integers[i]) return 8;
                }
                var caught = 0;
                try { Single(1, 2, 3, true); }
                catch (InvalidOperationException) { caught++; }
                try { Double(1, 2, 3, true); }
                catch (InvalidOperationException) { caught++; }
                try { Integer(1, 2, 3, true); }
                catch (InvalidOperationException) { caught++; }
                if (caught != 3 || s_calls != singles.Length * 4 + integers.Length * 2 + 3) return 5;
                // A normal invocation after unwinding must still have a valid frame.
                if (Double(1, 2, 3, false) != 2 || Single(1, 2, 3, false) != 2) return 6;
                if (Integer(1, 2, 3, false) != 2) return 9;
                return 0;
            }
        }
        """;
}
