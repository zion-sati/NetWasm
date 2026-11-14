using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericMathConversionCompilationTests
{
    // Every primitive's INumberBase conversions follow upstream: TryConvertFrom* handles the types of its own
    // sign (and ConvertTo the opposite), each answering false for a type it does not know. NetWasm's earlier
    // shortcut answered TryConvertTo* with TOther.CreateTruncating(value), so a pair neither side knew
    // (Int128 from uint, UInt128 from int) recursed until the stack overflowed; Int128.Parse reached it
    // through the generic integer parser's digit conversion.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PrimitiveConversionsFollowUpstreamAcrossSignsAndWidths(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            using System.Globalization;
            using System.Numerics;
            namespace GenericMathConversionFixture;
            public static class EntryPoint
            {
                private static T Create<T>(int value) where T : INumberBase<T> => T.CreateTruncating(value);
                private static T CreateUnsigned<T>(uint value) where T : INumberBase<T> => T.CreateTruncating(value);
                private static T Saturate<T>(int value) where T : INumberBase<T> => T.CreateSaturating(value);
                private static bool Throws<T>(Func<object> action) where T : Exception
                {
                    try { action(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    // Wide integers from the opposite sign, the case the generic parser needs.
                    if (Int128.CreateTruncating(uint.MaxValue) != 4294967295) return 1;
                    if (UInt128.CreateTruncating(-1) != UInt128.MaxValue) return 2;
                    if (UInt128.CreateTruncating(-1L) != UInt128.MaxValue) return 3;
                    if (UInt128.CreateSaturating(-5) != UInt128.Zero) return 4;
                    if (!Throws<OverflowException>(() => UInt128.CreateChecked(-1))) return 5;
                    if (Int128.CreateChecked(ulong.MaxValue) != (Int128)ulong.MaxValue) return 6;
                    if (Create<Int128>(7) != 7 || Create<UInt128>(7) != 7 || Create<nint>(7) != 7 || Create<nuint>(7) != 7) return 7;
                    if (CreateUnsigned<Int128>(9) != 9 || CreateUnsigned<nint>(9) != 9 || CreateUnsigned<short>(9) != 9) return 8;

                    // Narrow targets truncate, saturate and check as upstream.
                    if (int.CreateTruncating((Int128)(-1)) != -1) return 9;
                    if (int.CreateSaturating(Int128.MaxValue) != int.MaxValue) return 10;
                    if (int.CreateSaturating(UInt128.MaxValue) != int.MaxValue) return 11;
                    if (!Throws<OverflowException>(() => int.CreateChecked(uint.MaxValue))) return 12;
                    if (byte.CreateTruncating(0x1FF) != 0xFF || byte.CreateSaturating(-3) != 0) return 13;
                    // char has no Create* of its own; INumberBase's reach its conversions.
                    if (Create<char>(0x10041) != 'A' || Saturate<char>(-1) != '\0') return 14;
                    if (short.CreateTruncating(70000u) != unchecked((short)70000)) return 15;
                    if (ushort.CreateSaturating(-1) != 0 || ushort.CreateSaturating(1_000_000) != ushort.MaxValue) return 16;
                    if (long.CreateTruncating(UInt128.MaxValue) != -1L) return 17;
                    if (ulong.CreateSaturating(Int128.MinValue) != 0UL) return 18;
                    if (nint.CreateTruncating(-1L) != -1 || nuint.CreateSaturating(-1) != 0) return 19;

                    // Floating conversions keep upstream's saturating semantics and infinities.
                    if (float.CreateTruncating(Int128.MaxValue) != 1.7014118E+38f) return 20;
                    if (double.CreateSaturating(UInt128.MaxValue) != 3.402823669209385E+38) return 21;
                    if (int.CreateSaturating(double.PositiveInfinity) != int.MaxValue || int.CreateSaturating(double.NaN) != 0) return 22;
                    // The double-to-int conversion saturates, as desktop .NET's CreateTruncating does.
                    if (int.CreateTruncating(1e20) != int.MaxValue || int.CreateTruncating(-1e20) != int.MinValue) return 23;
                    if (!Throws<OverflowException>(() => int.CreateChecked(1e20))) return 24;
                    if (Half.CreateChecked(3) != (Half)3 || decimal.CreateTruncating(Int128.CreateTruncating(5)) != 5m) return 25;

                    // The parser's digit conversions, which reached the recursion.
                    if (Int128.Parse("-170141183460469231731687303715884105728") != Int128.MinValue) return 26;
                    if (Int128.Parse("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber) != -1) return 27;
                    if (!Int128.TryParse("\t42\r", out var wide) || wide != 42) return 28;
                    if (!UInt128.TryParse("340282366920938463463374607431768211455", out var unsignedWide) || unsignedWide != UInt128.MaxValue) return 29;
                    return input + 1;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("GenericMathConversionFixture", source)
            : assets.CompileSource("GenericMathConversionFixture", source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "GenericMathConversionFixture.EntryPoint",
            "Run", [new("run", "GenericMathConversionFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
