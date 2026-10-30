namespace NetWasm.Compiler.Tests.Correctness;

// Unsafe.BitCast between floating and integer primitives of one size, between
// integers of one width, between Vector128 and between Vector instantiations
// reinterprets in place; between laid-out types it keeps its call. Every cast
// matches desktop bit for bit, on special and ordinary values, and a delegate
// to BitConverter, whose own body is a BitCast, still converts.
[Collection(CorrectnessTestGroup.Name)]
public sealed class BitCastCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void BitCastsMatchDesktop() =>
        runner.Run(new(
            "BitCastCorpus",
            "NetWasm.Correctness.BitCast",
            """
            using System;
            using System.Numerics;
            using System.Runtime.CompilerServices;
            using System.Runtime.Intrinsics;

            namespace NetWasm.Correctness.BitCast;

            public static class EntryPoint
            {
                private static readonly double[] Doubles = [0.0, -0.0, 1.5, -2.75, double.NaN, double.PositiveInfinity, double.Epsilon, 1e300];
                private static readonly float[] Singles = [0f, -0f, 1.5f, -2.75f, float.NaN, float.NegativeInfinity, float.Epsilon, 3e38f];

                public static int Run(int input)
                {
                    var d = Doubles[input % Doubles.Length];
                    var f = Singles[input % Singles.Length];
                    var hash = 17L;
                    hash = hash * 31 + (long)Unsafe.BitCast<double, ulong>(d);
                    hash = hash * 31 + Unsafe.BitCast<double, long>(d);
                    hash = hash * 31 + BitConverter.DoubleToInt64Bits(Unsafe.BitCast<ulong, double>((ulong)input * 0x9E3779B97F4A7C15UL));
                    hash = hash * 31 + BitConverter.DoubleToInt64Bits(Unsafe.BitCast<long, double>(input - 3L));
                    hash = hash * 31 + Unsafe.BitCast<float, uint>(f);
                    hash = hash * 31 + Unsafe.BitCast<float, int>(f);
                    hash = hash * 31 + BitConverter.SingleToInt32Bits(Unsafe.BitCast<uint, float>((uint)input * 2654435761u));
                    hash = hash * 31 + BitConverter.SingleToInt32Bits(Unsafe.BitCast<int, float>(input - 3));
                    hash = hash * 31 + Unsafe.BitCast<int, uint>(input - 3);
                    hash = hash * 31 + Unsafe.BitCast<uint, int>((uint)input * 2654435761u);
                    hash = hash * 31 + Unsafe.BitCast<int, int>(input - 3) + Unsafe.BitCast<uint, uint>((uint)input);
                    hash = hash * 31 + (long)Unsafe.BitCast<long, ulong>(input - 3L);
                    hash = hash * 31 + Unsafe.BitCast<ulong, long>((ulong)input * 0x9E3779B97F4A7C15UL);
                    hash = hash * 31 + Unsafe.BitCast<long, long>(input - 3L) + (long)Unsafe.BitCast<ulong, ulong>((ulong)input);
                    var text = Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<ushort>>("NetWasm".AsSpan(input % 4));
                    hash = hash * 31 + text[0] + text.Length;
                    hash = hash * 31 + BitConverter.DoubleToInt64Bits(Unsafe.BitCast<double, double>(d));
                    hash = hash * 31 + BitConverter.SingleToInt32Bits(Unsafe.BitCast<float, float>(f));
                    var lanes = Unsafe.BitCast<Vector128<float>, Vector128<int>>(Vector128.Create(f, 1f, -1f, input));
                    hash = hash * 31 + lanes.GetElement(0) + lanes.GetElement(3);
                    var wide = Unsafe.BitCast<Vector<int>, Vector<uint>>(new Vector<int>(input - 3));
                    hash = hash * 31 + wide[0];
                    var plane = Unsafe.BitCast<Vector128<float>, Vector4>(Vector128.Create(1f, 2f, input, f));
                    hash = hash * 31 + (long)(plane.X + plane.Z);
                    hash = hash * 31 + Unsafe.BitCast<short, ushort>((short)(input - 3));
                    Func<double, long> bits = BitConverter.DoubleToInt64Bits;
                    hash = hash * 31 + bits(d);
                    hash = hash * 31 + (long)BitConverter.DoubleToUInt64Bits(Math.Log(input + 0.5));
                    return (int)(hash ^ (hash >> 32));
                }

                public static int Trace() => 0;
            }
            """,
            [.. Enumerable.Range(0, 16)])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
