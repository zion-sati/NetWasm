// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// NFloat is float on wasm32 and double on wasm64, as on desktop .NET's 32-bit and 64-bit platforms, from one CoreLib:
// its size and bits are the native type's, and arithmetic, rounding, parsing, formatting and generic math follow it.
// TUnit's argument formatter tests values against it.
public sealed class NFloatCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NFloatIsTheNativeFloatingPointType(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"NFloats{target}", """
            using System;
            using System.Globalization;
            using System.Numerics;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class EntryPoint
            {
                private static T Sum<T>(T[] values) where T : INumber<T>
                {
                    var sum = T.Zero;
                    foreach (var value in values) sum += value;
                    return sum;
                }

                public static int Run(int input)
                {
                    var wide = IntPtr.Size == 8;
                    if (NFloat.Size != IntPtr.Size || Unsafe.SizeOf<NFloat>() != IntPtr.Size) return 1;
                    var third = (NFloat)1 / 3;
                    var tenth = (NFloat)0.1;
                    if (wide ? Unsafe.As<NFloat, long>(ref tenth) != BitConverter.DoubleToInt64Bits(0.1)
                             : Unsafe.As<NFloat, int>(ref tenth) != BitConverter.SingleToInt32Bits(0.1f)) return 2;
                    var invariant = CultureInfo.InvariantCulture;
                    if (third.ToString(invariant) != (wide ? "0.3333333333333333" : "0.33333334")) return 3;
                    if ((double)(tenth + (NFloat)0.2) != (wide ? 0.1 + 0.2 : (double)(0.1f + 0.2f))) return 4;
                    if ((double)NFloat.Parse("16777217", invariant) != (wide ? 16777217.0 : 16777216.0)) return 5;
                    if ((double)NFloat.MaxValue != (wide ? double.MaxValue : float.MaxValue)) return 6;
                    if (!NFloat.IsNaN(NFloat.NaN) || NFloat.IsFinite(NFloat.PositiveInfinity)) return 7;
                    if (NFloat.Sqrt(2).ToString(invariant) != (wide ? "1.4142135623730951" : "1.4142135")) return 8;
                    if (Sum(new NFloat[] { 1.5f, 2.25f, -0.75f }) != 3) return 9;
                    object boxed = third;
                    if (boxed is not NFloat || boxed is not IFormattable formattable ||
                        formattable.ToString("F2", invariant) != "0.33") return 10;
                    if ((int)(NFloat)(-7.9) != -7 || (long)(NFloat)1e10 != 10000000000) return 11;
                    if (NFloat.CreateSaturating(1e300) != (wide ? (NFloat)1e300 : NFloat.PositiveInfinity)) return 12;
                    if (nuint.CreateSaturating(NFloat.MaxValue) != nuint.MaxValue) return 13;
                    var parsed = NFloat.TryParse("2.5", NumberStyles.Float, invariant, out var half);
                    if (!parsed || half * 2 != 5 || third.CompareTo(tenth) <= 0 || !third.Equals((NFloat)1 / 3)) return 14;
                    return input + 1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
