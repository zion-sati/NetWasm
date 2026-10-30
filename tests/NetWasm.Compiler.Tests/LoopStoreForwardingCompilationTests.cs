using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A loop that updates one element in place reads it back from a local: a
// double accumulated over an inner loop, a long element at a constant index
// read before the loop, a float element, a byte and a signed byte whose loads
// extend the stored value, a span parameter's element (also through upstream's
// indexer shape, with a throw helper and a conv.u index), and a short through a
// ref local. The results match desktop when the source array is the result, so
// other loads read updated elements, and when the loop throws part way
// through, so the element holds every store. A loop that calls a method,
// stores a second element, has nothing before it accessing the element, or
// moves its span keeps reading memory.
public sealed class LoopStoreForwardingCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        // A span-like view whose indexer is upstream Span<T>'s: a throw helper for the bounds
        // check and an index that conv.u widens.
        public readonly ref struct ByteView
        {
            private readonly ref byte _reference;
            private readonly int _length;

            public ByteView(byte[] array)
            {
                _reference = ref MemoryMarshal.GetArrayDataReference(array);
                _length = array.Length;
            }

            public int Length => _length;

            public ref byte this[int index]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    if ((uint)index >= (uint)_length)
                        Fail();
                    return ref Unsafe.Add(ref _reference, (nint)(uint)index);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fail() => throw new IndexOutOfRangeException();
        }

        public static class EntryPoint
        {
            private static int s_calls;

            private static double A(int i, int j) => 1.0 / ((i + j) * (i + j + 1) / 2 + i + 1);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Note(int value)
            {
                s_calls++;
                return value;
            }

            private static int Hash(double[] values)
            {
                var hash = 17L;
                foreach (var value in values) hash = hash * 31 + BitConverter.DoubleToInt64Bits(value) % 1000003;
                return (int)(hash ^ (hash >> 32));
            }

            // Forwarded: the element is stored before the loop and updated in place.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Multiply(int n, double[] v, double[] av)
            {
                for (var i = 0; i < n; i++)
                {
                    av[i] = 0;
                    for (var j = 0; j < n; j++) av[i] += A(i, j) * v[j];
                }
            }

            // Forwarded: a constant element read before the loop, loaded and stored whole.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Totals(long[] totals, int[] values)
            {
                var first = totals[2];
                for (var k = 0; k < values.Length; k++) totals[2] = totals[2] * 3 + values[k];
                return totals[2] - first;
            }

            // Forwarded: a float element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Scaled(float[] sums, int slot, float[] values)
            {
                sums[slot] = 1f;
                for (var k = 0; k < values.Length; k++) sums[slot] = sums[slot] * 0.5f + values[k];
                return sums[slot];
            }

            // Kept: the loop calls a method that could write the element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Called(int[] counts, int[] values)
            {
                s_calls = 0;
                counts[1] = 0;
                for (var k = 0; k < values.Length; k++) counts[1] += Note(values[k]);
                return counts[1] + s_calls;
            }

            // Kept: the loop also stores another element, which may be the same one.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoStores(int[] counts, int other, int[] values)
            {
                counts[0] = 5;
                for (var k = 0; k < values.Length; k++)
                {
                    counts[0] += values[k];
                    counts[other] = k;
                }
                return counts[0] * 7 + counts[other];
            }

            // Kept: nothing before the loop accesses the element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NoEntry(int[] counts, int slot, int[] values)
            {
                for (var k = 0; k < values.Length; k++) counts[slot] += values[k];
                return counts[slot];
            }

            // Kept: a narrow element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Narrow(byte[] bytes, int[] values)
            {
                bytes[3] = 1;
                for (var k = 0; k < values.Length; k++) bytes[3] += (byte)values[k];
                return bytes[3];
            }

            // Forwarded: a span parameter's element, as Span.IndexerBench updates it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SpanXor(Span<byte> data)
            {
                var first = data[0];
                for (var k = 1; k < data.Length; k++) data[0] ^= data[k];
                var result = data[0];
                data[0] = first;
                return result * 1000 + data[0];
            }

            // Forwarded: the same update through upstream's indexer shape.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Widened(ByteView data)
            {
                var first = data[0];
                for (var k = 1; k < data.Length; k++) data[0] ^= data[k];
                var result = data[0];
                data[0] = first;
                return result * 1000 + data[0];
            }

            // Forwarded: a signed byte, read back sign-extended.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Signed(sbyte[] values, int[] steps)
            {
                values[1] = -3;
                for (var k = 0; k < steps.Length; k++) values[1] = (sbyte)(values[1] * 5 + steps[k]);
                return values[1];
            }

            // Forwarded: a short through a ref local.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Through(short[] values, int[] steps)
            {
                ref var slot = ref values[2];
                slot = 7;
                for (var k = 0; k < steps.Length; k++) slot += (short)(steps[k] * 300);
                return slot;
            }

            // Kept: the loop moves the span.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Moving(Span<int> data)
            {
                data[0] = 1;
                var sum = 0;
                for (var k = 0; k < 3 && data.Length > 1; k++)
                {
                    data[0] += 2;
                    sum = sum * 7 + data[0];
                    data = data[1..];
                }
                return sum;
            }

            public static int Run(int input)
            {
                var n = input % 1000;
                switch (input / 1000)
                {
                    case 0:
                    {
                        var v = new double[n];
                        var av = new double[n];
                        for (var i = 0; i < n; i++) v[i] = 1 + i * 0.25;
                        Multiply(n, v, av);
                        return Hash(av);
                    }
                    case 1:
                    {
                        // The source is the result: each sum reads the elements it already updated.
                        var v = new double[n];
                        for (var i = 0; i < n; i++) v[i] = 1 + i * 0.5;
                        Multiply(n, v, v);
                        return Hash(v);
                    }
                    case 2:
                    {
                        // The source is shorter, so the inner loop throws part way through a sum.
                        var v = new double[n / 2];
                        var av = new double[n];
                        for (var i = 0; i < v.Length; i++) v[i] = 2 + i;
                        try { Multiply(n, v, av); }
                        catch (IndexOutOfRangeException) { return Hash(av) ^ 0x5a5a; }
                        return Hash(av);
                    }
                    case 3:
                    {
                        var totals = new long[] { 1, 2, n, 4 };
                        var values = new int[n];
                        for (var k = 0; k < n; k++) values[k] = k * 7 - 11;
                        return (int)(Totals(totals, values) % 1000000007);
                    }
                    case 4:
                    {
                        var values = new float[n];
                        for (var k = 0; k < n; k++) values[k] = k * 0.375f - 1;
                        return BitConverter.SingleToInt32Bits(Scaled(new float[4], n % 4, values));
                    }
                    case 5:
                    {
                        var values = new int[n];
                        for (var k = 0; k < n; k++) values[k] = k + 3;
                        return Called(new int[2], values);
                    }
                    case 6:
                    {
                        var values = new int[n];
                        for (var k = 0; k < n; k++) values[k] = k * 5;
                        return TwoStores(new int[3], n % 3, values);
                    }
                    case 7:
                    {
                        var values = new int[n];
                        for (var k = 0; k < n; k++) values[k] = k - 2;
                        return NoEntry(new int[] { 9, 8, 7 }, n % 3, values);
                    }
                    case 8:
                    {
                        var values = new int[n];
                        for (var k = 0; k < n; k++) values[k] = k * 37;
                        return Narrow(new byte[5], values);
                    }
                    case 9:
                    {
                        var data = new byte[n + 1];
                        for (var k = 0; k < data.Length; k++) data[k] = (byte)(k * 29 + 5);
                        return SpanXor(data);
                    }
                    case 10:
                    {
                        var steps = new int[n];
                        for (var k = 0; k < n; k++) steps[k] = k * 11 - 40;
                        return Signed(new sbyte[3], steps);
                    }
                    case 11:
                    {
                        var steps = new int[n];
                        for (var k = 0; k < n; k++) steps[k] = k * 7 + 3;
                        return Through(new short[4], steps);
                    }
                    case 13:
                    {
                        var data = new byte[n + 1];
                        for (var k = 0; k < data.Length; k++) data[k] = (byte)(k * 29 + 5);
                        return Widened(new ByteView(data));
                    }
                    default:
                    {
                        var data = new int[n + 2];
                        for (var k = 0; k < data.Length; k++) data[k] = k * 3;
                        return Moving(data);
                    }
                }
            }
        }
        """;

    private static readonly string[] Forwarded = ["Multiply", "Narrow", "Scaled", "Signed", "SpanXor", "Through", "Totals", "Widened"];

    // As desktop .NET computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, 17 },
        { 7, 2069568182 },
        { 40, -1557426933 },
        { 1001, 527 },
        { 1007, 945659154 },
        { 1040, -1603693842 },
        { 2001, 22613 },
        { 2007, -352061861 },
        { 2040, -1738698856 },
        { 3001, -9 },
        { 3007, 7080 },
        { 3040, -170175876 },
        { 4000, 1065353216 },
        { 4007, 1071939584 },
        { 4040, 1104412672 },
        { 5007, 49 },
        { 5040, 940 },
        { 6007, 776 },
        { 6009, 64 },
        { 6040, 27374 },
        { 7007, 15 },
        { 7040, 708 },
        { 8007, 10 },
        { 8040, 189 },
        { 9001, 39005 },
        { 9007, 200005 },
        { 9040, 165005 },
        { 10001, -55 },
        { 10007, 124 },
        { 10040, 97 },
        { 11007, -15129 },
        { 11040, -29929 },
        { 12001, 26 },
        { 12040, 190 },
        { 13001, 39005 },
        { 13007, 200005 },
        { 13040, 165005 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3ForwardsOnlyElementsTheLoopAloneWrites(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"LoopStoreForwarding_{target}", Source);

        var forwarded = Compile(assets, assembly, target, FinalWasmOptimization.O3, null);
        var kept = Compile(assets, assembly, target, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(LoopStoreForwarding: ManagedLoopStoreForwarding.Disabled));

        var before = ElementLoads(kept);
        var after = ElementLoads(forwarded);
        Assert.Equal(Forwarded, before.Keys.Where(method => after[method] < before[method]).Order());
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                forwarded.ApplicationModule, assets.Directory, input, target, forwarded.StaticDataEnd));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ForwardedElementsMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("LoopStoreForwardingOz", Source);
        var result = Compile(assets, assembly, WasmTarget.Wasm32, FinalWasmOptimization.Oz, null);

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // How many array element loads each EntryPoint method keeps.
    private static Dictionary<string, int> ElementLoads(CompilationResult result) =>
        result.Program.Methods.Values
            .Where(static method => method.Method.DeclaringType.FullName == "EntryPoint")
            .ToDictionary(
                static method => method.Method.Definition.Name,
                static method => method.Body.Instructions.Count(static instruction =>
                    instruction.Operation is CilOperation.LoadArrayElement or CilOperation.LoadObject));

    private static CompilationResult Compile(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        FinalWasmOptimization optimization,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: optimization, OptimizationOverrides: overrides));
}
