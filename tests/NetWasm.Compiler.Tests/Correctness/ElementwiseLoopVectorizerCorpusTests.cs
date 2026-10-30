using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A counted loop that only stores lane-wise expressions of elements at the
// counter runs as Vector128 operations ahead of the loop when every access is in
// bounds. Integer, unsigned, long, float and double elements, the counter's own
// value, constants and invariants, pure Math functions, every remainder length,
// in-place updates and a stencil, whose pure Math call of values the loop does
// not change is computed once ahead of it, match desktop, and so do the loops the vector
// form must not change: one array read at another offset than it is written
// (here the same array twice), loops that leave their arrays or meet a null array
// part way, which a catch observes, and a counter copy read after the loop.
[Collection(CorrectnessTestGroup.Name)]
public sealed class ElementwiseLoopVectorizerCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.ElementwiseLoops;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hash = 17;
                for (var size = 0; size <= 21; size++)
                {
                    hash = Mix(hash, input switch
                    {
                        0 => Counters(size),
                        1 => Integers(Ints(size, 3), Ints(size, 7), size),
                        2 => Unsigned(UInts(size)),
                        3 => (int)Longs(Longs(size, 5), Longs(size, 11)),
                        4 => Floats(Floats(size, 0.5f), Floats(size, 1.25f)),
                        5 => Stencil(Doubles(size + 2, 0.75), size + 2),
                        6 => StencilInPlace(Doubles(size + 2, 0.75), size + 2),
                        7 => InPlace(Ints(size, 13)),
                        8 => PastTheEnd(Ints(size, 1)),
                        9 => NullArray(size),
                        10 => LateStart(Ints(size + 6, 2), 5),
                        11 => Bounded(),
                        12 => CopyAfterLoop(Ints(size, 4)),
                        _ => Offsets(Ints(size + 4, 9), Ints(size + 4, 2), size),
                    });
                }
                return hash;
            }

            // AddArray's shape: several arrays from the counter's value.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Counters(int size)
            {
                var a = new int[size + 1];
                var b = new int[size + 1];
                var c = new int[size + 1];
                int j, k, l;
                for (j = 0; j < a.Length; j++)
                {
                    a[j] = 70000 + j;
                    k = j;
                    b[k] = a[j] + k + k;
                    l = j;
                    c[l] = b[k] + l * 3 - (j << 2);
                }
                for (j = 0; j < a.Length; j++)
                {
                    k = j;
                    l = j;
                    a[j] = a[j] + b[k] + c[l] - b[k - j + l];
                }
                return Sum(a) * 7 + Sum(b) * 3 + Sum(c);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Integers(int[] a, int[] b, int factor)
            {
                var c = new int[a.Length];
                for (var i = 0; i < c.Length; i++) c[i] = (a[i] + b[i] * 3 - (a[i] >> 2)) ^ factor | (b[i] & 0x55);
                for (var i = 0; i < c.Length; i++) c[i] = -c[i] + ~a[i] * factor;
                return Sum(c);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Unsigned(uint[] a)
            {
                var c = new uint[a.Length];
                for (var i = 0; i < c.Length; i++) c[i] = (a[i] >> 3) + (a[i] << 5) * 3u;
                var hash = 0;
                foreach (var value in c) hash = hash * 31 + (int)value;
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Longs(long[] a, long[] b)
            {
                var c = new long[a.Length];
                for (var i = 0; i < c.Length; i++) c[i] = a[i] * b[i] + 7L - (b[i] >> 1);
                long total = 0;
                foreach (var value in c) total = total * 31 + value;
                return total ^ (total >> 32);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Floats(float[] a, float[] b)
            {
                var c = new float[a.Length];
                var scale = a.Length + 0.25f;
                for (var i = 0; i < c.Length; i++) c[i] = a[i] * scale + b[i] / 2f - -a[i];
                var hash = 0;
                foreach (var value in c) hash = hash * 31 + BitConverter.SingleToInt32Bits(value);
                return hash;
            }

            // Burgers' update: the stencil reads un at -1, 0 and +1, and the
            // power of values the loop does not change is computed once.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Stencil(double[] un, int nx)
            {
                var u = new double[nx];
                double dt = 0.01, dx = 0.5, nu = 0.07;
                for (var i = 1; i < nx - 1; i++)
                {
                    u[i] = un[i] - un[i] * dt / dx * (un[i] - un[i - 1]) + Math.Pow(nu * dt / dx, 2.0) *
                        (un[i + 1] - 2 * un[i] + un[i - 1]);
                }
                return Hash(u);
            }

            // The same stencil with one array as both: each iteration reads the
            // element the one before wrote, so the vector loop must not run.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StencilInPlace(double[] u, int nx) => StencilInto(u, u, nx);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StencilInto(double[] u, double[] un, int nx)
            {
                for (var i = 1; i < nx - 1; i++) u[i] = un[i] * 0.5 + (un[i + 1] + un[i - 1]) * 0.25;
                return Hash(u);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InPlace(int[] a)
            {
                for (var i = 0; i < a.Length; i++) a[i] = a[i] * 2 + i;
                return Sum(a);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PastTheEnd(int[] a)
            {
                var b = new int[a.Length + 3];
                var i = 0;
                try
                {
                    for (i = 0; i < a.Length + 2; i++) b[i] = a[i] + 1;
                }
                catch (IndexOutOfRangeException)
                {
                    return Sum(b) * 100 + i;
                }
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NullArray(int size)
            {
                int[]? a = size % 3 == 0 ? null : Ints(size, 6);
                var b = new int[size];
                var i = 0;
                try
                {
                    for (i = 0; i < size; i++) b[i] = a![i] * 3;
                }
                catch (NullReferenceException)
                {
                    return 1000 + i;
                }
                return Sum(b);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LateStart(int[] a, int start)
            {
                var b = new int[a.Length];
                for (var i = start; i < a.Length; i++) b[i] = a[i] - start;
                return Sum(b);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Bounded()
            {
                var a = new int[40];
                for (var i = 0; i <= 37; i++) a[i] = i * i;
                return Sum(a);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyAfterLoop(int[] a)
            {
                var b = new int[a.Length];
                var k = -5;
                for (var i = 0; i < a.Length; i++)
                {
                    k = i + 2;
                    b[i] = a[i] + k;
                }
                return Sum(b) * 10 + k;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Offsets(int[] a, int[] b, int size)
            {
                var c = new int[size + 4];
                for (var i = 2; i < size + 2; i++) c[i + 1] = a[i - 2] + b[i + 2] - a[i];
                return Sum(c);
            }

            private static int Sum(int[] values)
            {
                var total = 0;
                foreach (var value in values) total = total * 31 + value;
                return total;
            }

            private static int Hash(double[] values)
            {
                var hash = 0L;
                foreach (var value in values) hash = hash * 31 + BitConverter.DoubleToInt64Bits(value);
                return (int)(hash ^ (hash >> 32));
            }

            private static int[] Ints(int size, int seed)
            {
                var data = new int[size];
                for (var index = 0; index < size; index++) data[index] = index * seed * 7919 - 1000;
                return data;
            }

            private static uint[] UInts(int size)
            {
                var data = new uint[size];
                for (var index = 0; index < size; index++) data[index] = (uint)index * 2654435761u;
                return data;
            }

            private static long[] Longs(int size, long seed)
            {
                var data = new long[size];
                for (var index = 0; index < size; index++) data[index] = index * seed * 0x9E3779B97F4A7C1L;
                return data;
            }

            private static float[] Floats(int size, float seed)
            {
                var data = new float[size];
                for (var index = 0; index < size; index++) data[index] = index * seed - 3.5f;
                return data;
            }

            private static double[] Doubles(int size, double seed)
            {
                var data = new double[size];
                for (var index = 0; index < size; index++) data[index] = index * 37 % 11 * seed - 1.5;
                return data;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void ElementwiseLoopsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ElementwiseLoopVectorizerCorpus",
            "NetWasm.Correctness.ElementwiseLoops",
            Source,
            [.. Enumerable.Range(0, 14)])
        {
            Optimization = FinalWasmOptimization.O3,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EveryElementwiseLoopRunsAsVectorsAtO3(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ElementwiseLoops" + target, Source);

        var vectorized = Compile(assets, assembly, target, null);
        var scalar = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(LoopVectorization: ManagedLoopVectorization.Disabled));

        // The loops above, and the input generators whose elements are integer
        // expressions of the counter.
        string[] loops =
        [
            "Bounded", "CopyAfterLoop", "Counters", "Floats", "InPlace", "Integers", "Ints", "LateStart", "Longs",
            "NullArray", "Offsets", "PastTheEnd", "Stencil", "StencilInto", "UInts", "Unsigned",
        ];
        var actual = string.Join(", ", Vectorized(vectorized));
        Assert.True(string.Join(", ", loops) == actual, "vectorized: " + actual);
        Assert.Empty(Vectorized(scalar));
        foreach (var input in (int[])[0, 5, 6, 8])
        {
            Assert.Equal(
                CompilerTestSupport.ExecuteWithStandardWasiNode(scalar.ApplicationModule, assets.Directory, input, target, scalar.StaticDataEnd),
                CompilerTestSupport.ExecuteWithStandardWasiNode(vectorized.ApplicationModule, assets.Directory, input, target, vectorized.StaticDataEnd));
        }
    }

    // The EntryPoint methods that store a Vector128 into an array.
    private static string[] Vectorized(CompilationResult result) =>
        [.. result.Program.Methods.Values
            .Where(static method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.ElementwiseLoops.EntryPoint" &&
                method.Body.Instructions.Any(static instruction => instruction.Operand is CilOperand.MethodInstance
                {
                    Value:
                    {
                        DeclaringType.FullName: "System.Runtime.Intrinsics.Vector128",
                        Definition.Name: "StoreUnsafe",
                    },
                }))
            .Select(static method => method.Method.Definition.Name)
            .Order(StringComparer.Ordinal)];

    private static CompilationResult Compile(TestAssets assets, string assembly, WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.ElementwiseLoops.EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));
}
