using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Unsafe.BitCast between values Wasm holds in registers reinterprets them in
// place. Read through the address of a copy instead, every caller, such as
// fdlibm's Math.Log through BitConverter.DoubleToUInt64Bits, allocates a value
// frame on every call.
public sealed partial class BitCastFrameCompilationTests
{
    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static ulong DoubleBits(double value) => Unsafe.BitCast<double, ulong>(value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float SingleOf(int value) => Unsafe.BitCast<int, float>(value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static uint Unsigned(int value) => Unsafe.BitCast<int, uint>(value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Vector128<int> Lanes(Vector128<float> value) => Unsafe.BitCast<Vector128<float>, Vector128<int>>(value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double Log(double value) => Math.Log(value);

            // Every kind of cast, each checked against its plain equivalent.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Casts(int input)
            {
                var d = input * 0.75 + 0.5;
                var f = (float)d;
                if (Unsafe.BitCast<double, long>(d) != BitConverter.DoubleToInt64Bits(d)) return 1;
                if (Unsafe.BitCast<ulong, double>((ulong)input) != BitConverter.Int64BitsToDouble(input)) return 2;
                if (Unsafe.BitCast<long, double>(input) != BitConverter.Int64BitsToDouble(input)) return 3;
                if (Unsafe.BitCast<float, uint>(f) != (uint)BitConverter.SingleToInt32Bits(f)) return 4;
                if (Unsafe.BitCast<float, int>(f) != BitConverter.SingleToInt32Bits(f)) return 5;
                if (Unsafe.BitCast<uint, float>((uint)input) != BitConverter.Int32BitsToSingle(input)) return 6;
                if (Unsafe.BitCast<uint, int>(uint.MaxValue - (uint)input) != -1 - input) return 7;
                if (Unsafe.BitCast<ulong, long>(ulong.MaxValue) != -1L) return 8;
                if (Unsafe.BitCast<long, long>(input) != input || Unsafe.BitCast<ulong, ulong>(7UL) != 7UL) return 9;
                if (Unsafe.BitCast<int, int>(input) != input || Unsafe.BitCast<uint, uint>(7u) != 7u) return 10;
                if (Unsafe.BitCast<double, double>(d) != d || Unsafe.BitCast<float, float>(f) != f) return 11;
                if (Unsafe.BitCast<Vector<int>, Vector<uint>>(new Vector<int>(-1))[0] != uint.MaxValue) return 12;
                if (Unsafe.BitCast<Vector128<float>, Vector4>(Vector128.Create(1f, 2f, 3f, f)).W != f) return 13;
                if (Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<ushort>>("NetWasm".AsSpan())[1] != 'e') return 14;
                Func<double, long> bits = BitConverter.DoubleToInt64Bits;
                if (bits(d) != BitConverter.DoubleToInt64Bits(d)) return 15;
                if (Unsafe.BitCast<short, ushort>((short)-1) != ushort.MaxValue) return 16;
                return 0;
            }

            public static int Run(int input)
            {
                if (Casts(input) is var failed && failed != 0) return -100 - failed;
                var value = input * 0.75 + 0.5;
                if (DoubleBits(value) != BitConverter.DoubleToUInt64Bits(value)) return -1;
                if (SingleOf(input) != BitConverter.Int32BitsToSingle(input)) return -2;
                if (Unsigned(-input) != unchecked((uint)-input)) return -3;
                if (Lanes(Vector128.Create((float)value)).GetElement(2) != BitConverter.SingleToInt32Bits((float)value)) return -4;
                if (Log(value) != Math.Log(value)) return -5;
                return input + 1;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BitCastCallersNeedNoValueFrame(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"BitCastFrame_{target}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: FinalWasmOptimization.O3));

        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        string[] probes = ["$EntryPoint::DoubleBits", "$EntryPoint::SingleOf", "$EntryPoint::Unsigned", "$EntryPoint::Lanes", "$System.Math::Log"];
        var framed = FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ', StringComparison.Ordinal)], Body: function))
            .Where(function => probes.Contains(function.Name))
            .ToDictionary(static function => function.Name, static function => FrameEnter().IsMatch(function.Body));
        Assert.Equal(probes.ToDictionary(static probe => probe, static _ => false), framed);
        foreach (var input in new[] { 0, 1, 3, 40, 1000000 })
        {
            Assert.Equal(input + 1, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"call \$\S*value_frame_enter")]
    private static partial Regex FrameEnter();
}
