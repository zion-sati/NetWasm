using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A reduction loop folds an array's elements into a local: a sum, minimum,
// maximum, and, or or xor, each element converted to the local's type, perhaps
// through a narrower type first, as (long)(sbyte)a[i] narrows a long to its low
// byte before widening it again. Every fold over every integer element type,
// into int, uint, long and ulong through no cast or any narrower cast, computes
// what desktop does on values at both ends of each width and around each sign
// bit; a fold that cannot reorder its elements through the casts runs as a loop.
public sealed class ReductionConversionCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void FoldsConvertEachElementAsDesktopDoes(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ReductionConversionCorpus",
            "NetWasm.Correctness.ReductionConversions",
            Source,
            [.. Enumerable.Range(0, 48)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    // Generated: each input folds one operation over one element type, into
    // every accumulator type through every cast.
    private const string Source = """
            using System;
            using System.Numerics;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.ReductionConversions;

            // The or folds sign-extend on purpose.
            #pragma warning disable CS0675

            public static class EntryPoint
            {
                private static readonly long[] s_patterns =
                [
                    0, 1, -1, 2, -2, 0x7F, 0x80, 0xFF, 0x100, -0x80, -0x81, 0x7FFF, 0x8000, 0xFFFF, -0x8000, -0x8001,
                    0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF, -0x8000_0000L, -0x8000_0001L, long.MaxValue, long.MinValue,
                    0x5555_5555_5555_5555, unchecked((long)0xAAAA_AAAA_AAAA_AAAA), 0x1234_5678_9ABC_DEF0, 100, -100, 37,
                    160, 191, 31, -96, 3, 0x7FFF_FFFF_0000_0001, unchecked((long)0x8000_0000_FFFF_FFFF), 12345,
                ];

                // The patterns as the element type holds them: both ends of each
                // width and around each sign bit, 37 of them so a vector loop
                // leaves a tail.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T[] Data<T>() where T : IBinaryInteger<T>
                {
                    var data = new T[s_patterns.Length];
                    for (var i = 0; i < data.Length; i++) data[i] = T.CreateTruncating(s_patterns[i]);
                    return data;
                }

                public static int Run(int input) => Mix(unchecked(input switch
                {
                    0 => SumOfSbyte(Data<sbyte>()),
                    1 => SumOfByte(Data<byte>()),
                    2 => SumOfShort(Data<short>()),
                    3 => SumOfUshort(Data<ushort>()),
                    4 => SumOfInt(Data<int>()),
                    5 => SumOfUint(Data<uint>()),
                    6 => SumOfLong(Data<long>()),
                    7 => SumOfUlong(Data<ulong>()),
                    8 => MinOfSbyte(Data<sbyte>()),
                    9 => MinOfByte(Data<byte>()),
                    10 => MinOfShort(Data<short>()),
                    11 => MinOfUshort(Data<ushort>()),
                    12 => MinOfInt(Data<int>()),
                    13 => MinOfUint(Data<uint>()),
                    14 => MinOfLong(Data<long>()),
                    15 => MinOfUlong(Data<ulong>()),
                    16 => MaxOfSbyte(Data<sbyte>()),
                    17 => MaxOfByte(Data<byte>()),
                    18 => MaxOfShort(Data<short>()),
                    19 => MaxOfUshort(Data<ushort>()),
                    20 => MaxOfInt(Data<int>()),
                    21 => MaxOfUint(Data<uint>()),
                    22 => MaxOfLong(Data<long>()),
                    23 => MaxOfUlong(Data<ulong>()),
                    24 => AndOfSbyte(Data<sbyte>()),
                    25 => AndOfByte(Data<byte>()),
                    26 => AndOfShort(Data<short>()),
                    27 => AndOfUshort(Data<ushort>()),
                    28 => AndOfInt(Data<int>()),
                    29 => AndOfUint(Data<uint>()),
                    30 => AndOfLong(Data<long>()),
                    31 => AndOfUlong(Data<ulong>()),
                    32 => OrOfSbyte(Data<sbyte>()),
                    33 => OrOfByte(Data<byte>()),
                    34 => OrOfShort(Data<short>()),
                    35 => OrOfUshort(Data<ushort>()),
                    36 => OrOfInt(Data<int>()),
                    37 => OrOfUint(Data<uint>()),
                    38 => OrOfLong(Data<long>()),
                    39 => OrOfUlong(Data<ulong>()),
                    40 => XorOfSbyte(Data<sbyte>()),
                    41 => XorOfByte(Data<byte>()),
                    42 => XorOfShort(Data<short>()),
                    43 => XorOfUshort(Data<ushort>()),
                    44 => XorOfInt(Data<int>()),
                    45 => XorOfUint(Data<uint>()),
                    46 => XorOfLong(Data<long>()),
                    47 => XorOfUlong(Data<ulong>()),
                    _ => -1,
                }));

                private static int Mix(long hash) => (int)(hash ^ (hash >> 32));

            private static long SumOfSbyte(sbyte[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long SumOfByte(byte[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long SumOfShort(short[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long SumOfUshort(ushort[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long SumOfInt(int[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long SumOfUint(uint[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long SumOfLong(long[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long SumOfUlong(ulong[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m += (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m += (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m += (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m += (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MinOfSbyte(sbyte[] a)
            {
                long h = 17;
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MinOfByte(byte[] a)
            {
                long h = 17;
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MinOfShort(short[] a)
            {
                long h = 17;
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MinOfUshort(ushort[] a)
            {
                long h = 17;
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MinOfInt(int[] a)
            {
                long h = 17;
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MinOfUint(uint[] a)
            {
                long h = 17;
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MinOfLong(long[] a)
            {
                long h = 17;
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MinOfUlong(ulong[] a)
            {
                long h = 17;
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MaxValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Min(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MaxOfSbyte(sbyte[] a)
            {
                long h = 17;
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MaxOfByte(byte[] a)
            {
                long h = 17;
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MaxOfShort(short[] a)
            {
                long h = 17;
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MaxOfUshort(ushort[] a)
            {
                long h = 17;
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MaxOfInt(int[] a)
            {
                long h = 17;
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MaxOfUint(uint[] a)
            {
                long h = 17;
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MaxOfLong(long[] a)
            {
                long h = 17;
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long MaxOfUlong(ulong[] a)
            {
                long h = 17;
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = int.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (int)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = uint.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (uint)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = long.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (long)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(sbyte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(byte)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(short)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(ushort)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(int)a[i]);
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ulong.MinValue;
                    for (var i = 0; i < a.Length; i++) m = Math.Max(m, (ulong)(uint)a[i]);
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long AndOfSbyte(sbyte[] a)
            {
                long h = 17;
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long AndOfByte(byte[] a)
            {
                long h = 17;
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long AndOfShort(short[] a)
            {
                long h = 17;
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long AndOfUshort(ushort[] a)
            {
                long h = 17;
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long AndOfInt(int[] a)
            {
                long h = 17;
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long AndOfUint(uint[] a)
            {
                long h = 17;
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long AndOfLong(long[] a)
            {
                long h = 17;
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long AndOfUlong(ulong[] a)
            {
                long h = 17;
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = ~(int)0;
                    for (var i = 0; i < a.Length; i++) m &= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = ~(uint)0;
                    for (var i = 0; i < a.Length; i++) m &= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = ~(long)0;
                    for (var i = 0; i < a.Length; i++) m &= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = ~(ulong)0;
                    for (var i = 0; i < a.Length; i++) m &= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long OrOfSbyte(sbyte[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long OrOfByte(byte[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long OrOfShort(short[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long OrOfUshort(ushort[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long OrOfInt(int[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long OrOfUint(uint[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long OrOfLong(long[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long OrOfUlong(ulong[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m |= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long XorOfSbyte(sbyte[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long XorOfByte(byte[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long XorOfShort(short[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long XorOfUshort(ushort[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long XorOfInt(int[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long XorOfUint(uint[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long XorOfLong(long[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

            private static long XorOfUlong(ulong[] a)
            {
                long h = 17;
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    int m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (int)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    uint m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (uint)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    long m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (long)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(sbyte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(byte)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(short)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(ushort)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(int)a[i];
                    h = h * 1000003 + (long)m;
                }
                {
                    ulong m = 0;
                    for (var i = 0; i < a.Length; i++) m ^= (ulong)(uint)a[i];
                    h = h * 1000003 + (long)m;
                }
                return h;
            }

                public static int Trace() => 0;
            }
            """;
}
