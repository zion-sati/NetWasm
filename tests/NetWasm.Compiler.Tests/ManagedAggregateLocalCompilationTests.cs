using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ManagedAggregateLocalCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PreservesOverlappingLocalLanesAcrossWritingAndReadonlyCalls(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("AggregateLocalSynchronization", """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            [StructLayout(LayoutKind.Explicit, Size = 12)]
            public struct Buffer
            {
                [FieldOffset(0)] public uint A;
                [FieldOffset(4)] public uint B;
                [FieldOffset(8)] public uint C;
                [FieldOffset(0)] public ulong Low;
                [FieldOffset(4)] public ulong High;
            }

            public static class EntryPoint
            {
                public static int Run(int mode)
                {
                    try { return Worker(mode == 0 ? 3 : -1); }
                    catch (InvalidOperationException) { return mode == 1 ? 42 : 2; }
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Worker(int seed)
                {
                    Unsafe.SkipInit(out Buffer value);
                    if (value.A != 0 || value.B != 0 || value.C != 0) return 3;
                    value.Low = 0x1122334455667788UL;
                    value.C = 0x99AABBCC;
                    ref ulong overlap = ref value.High;
                    overlap ^= 0x0102030405060708UL;
                    ulong expectedHigh = 0x99AABBCC11223344UL ^ 0x0102030405060708UL;
                    if (value.High != expectedHigh || value.A != 0x55667788) return 4;
                    if (value.B != (uint)expectedHigh || value.C != (uint)(expectedHigh >> 32)) return 5;
                    uint expectedA = value.A + (uint)seed;
                    uint expectedC = value.C ^ expectedA;
                    Mutate(ref value, ref value, seed);
                    if (value.A != expectedA || value.C != expectedC) return 6;
                    if (Inspect(in value) != ((ulong)expectedC << 32 | (uint)expectedHigh)) return 7;
                    for (int index = 0; index < 3; index++)
                    {
                        value.Low += 0x100000001UL;
                        Mutate(ref value, ref value, index);
                    }
                    return value.High == Inspect(in value) ? 42 : 8;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Mutate(ref Buffer first, ref Buffer second, int seed)
                {
                    first.A += (uint)seed;
                    GC.Collect();
                    second.C ^= first.A;
                    if (seed < 0) throw new InvalidOperationException();
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static ulong Inspect(in Buffer value) => value.High;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 1, target, compilation.StaticDataEnd));
    }

    // Pair<short>.A = default is initobj of a short, two bytes of the lane it
    // shares with B; clearing it must leave B as it is.
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void ClearingANarrowFieldKeepsTheRestOfItsLane(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"AggregateNarrowClear_{target}_{preset}", """
            using System.Runtime.CompilerServices;

            public struct Pair<T>
            {
                public T A;
                public T B;
                public int C;
                public void ClearA() => A = default;
            }

            public static class EntryPoint
            {
                public static int Run(int seed) => Worker(seed);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Worker(int seed)
                {
                    var pair = new Pair<short>();
                    pair.A = (short)(seed + 1);
                    pair.B = (short)(seed + 2);
                    pair.C = seed + 3;
                    pair.ClearA();
                    return pair.A * 10000 + pair.B * 100 + pair.C;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: preset));

        foreach (var (seed, expected) in new[] { (1, 304), (5, 708) })
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
                assets.Directory, seed, target, compilation.StaticDataEnd));
        }
    }

    // Clearing an empty struct field covers no lane and clearing a field whose
    // struct ends in padding covers part of one, so neither splits the local.
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void ClearingAFieldThatCoversNoWholeLaneKeepsTheLocalWhole(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"AggregateFieldClear_{target}_{preset}", """
            using System.Runtime.CompilerServices;

            public struct Empty { }
            public struct Padded { public int A; public byte B; }
            public struct Holder { public int X; public Empty E; public Padded P; public int Y; }

            public static class EntryPoint
            {
                public static int Run(int seed) => Worker(seed);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Worker(int seed)
                {
                    Holder holder = default;
                    holder.X = seed;
                    holder.P.A = seed + 1;
                    holder.P.B = (byte)(seed + 2);
                    holder.Y = seed + 3;
                    holder.E = default;
                    var before = holder.X + holder.P.A + holder.P.B + holder.Y;
                    holder.P = default;
                    return before * 100 + holder.X + holder.P.A + holder.P.B + holder.Y;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: preset));

        foreach (var (seed, expected) in new[] { (1, 1005), (5, 2613) })
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
                assets.Directory, seed, target, compilation.StaticDataEnd));
        }
    }
}
