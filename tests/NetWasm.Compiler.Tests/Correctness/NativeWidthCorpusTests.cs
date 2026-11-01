namespace NetWasm.Compiler.Tests.Correctness;

// One CoreLib serves wasm32 and wasm64, so code upstream selects with TARGET_64BIT or
// TARGET_32BIT chooses by IntPtr.Size instead. Byte comparisons of 4 to 7 bytes, the
// native-width BitOperations overloads, native-width endianness reversal and saturating
// conversions to nint and nuint each give the result of their width. Desktop is 64-bit, so
// each check states its expectation in terms of IntPtr.Size and the program returns how many
// checks fail, zero on every target.
public sealed class NativeWidthCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NativeWidthCodeTakesItsTargetsBranch() =>
        runner.Run(new(
            "NativeWidthCorpus",
            "NetWasm.Correctness.NativeWidth",
            Source,
            [0, 1, 2, 3])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Buffers.Binary;
            using System.Numerics;

            namespace NetWasm.Correctness.NativeWidth;

            public static class EntryPoint
            {
                private static readonly ulong[] Samples =
                [
                    0, 1, 2, 3, 0x80, 0xFFFF, 0x1_0000, 0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF, 0x1_0000_0000,
                    0x1_0000_0001, 0x1234_5678_9ABC_DEF0, 0x8000_0000_0000_0000, 0xFFFF_FFFF_FFFF_FFFF,
                ];

                public static int Run(int input) => input switch
                {
                    0 => Comparisons(),
                    1 => Bits(),
                    2 => Endianness(),
                    _ => Saturation(),
                };

                // Spans that differ at one byte are unequal, at every length and position.
                private static int Comparisons()
                {
                    var failures = 0;
                    for (var length = 0; length <= 24; length++)
                    {
                        for (var position = -1; position < length; position++)
                        {
                            var left = new byte[length];
                            var right = new byte[length];
                            for (var index = 0; index < length; index++)
                                left[index] = right[index] = (byte)(index * 37 + 5);
                            if (position >= 0)
                                right[position] ^= (byte)(1 << (position % 8));
                            var equal = position < 0;
                            if (left.AsSpan().SequenceEqual(right) != equal) failures++;
                            if ((left.AsSpan().SequenceCompareTo(right) == 0) != equal) failures++;
                        }
                    }
                    return failures;
                }

                private static int Bits()
                {
                    var failures = 0;
                    var wide = IntPtr.Size == 8;
                    foreach (var sample in Samples)
                    {
                        var value = (nuint)sample;
                        if (BitOperations.LeadingZeroCount(value) != (wide ? BitOperations.LeadingZeroCount((ulong)value) : BitOperations.LeadingZeroCount((uint)value))) failures++;
                        if (BitOperations.TrailingZeroCount(value) != (wide ? BitOperations.TrailingZeroCount((ulong)value) : BitOperations.TrailingZeroCount((uint)value))) failures++;
                        if (BitOperations.TrailingZeroCount((nint)value) != (wide ? BitOperations.TrailingZeroCount((ulong)value) : BitOperations.TrailingZeroCount((uint)value))) failures++;
                        if (BitOperations.PopCount(value) != (wide ? BitOperations.PopCount((ulong)value) : BitOperations.PopCount((uint)value))) failures++;
                        if (value != 0 && BitOperations.Log2(value) != (wide ? BitOperations.Log2((ulong)value) : BitOperations.Log2((uint)value))) failures++;
                        if (BitOperations.RoundUpToPowerOf2(value) != (wide ? (nuint)BitOperations.RoundUpToPowerOf2((ulong)value) : (nuint)BitOperations.RoundUpToPowerOf2((uint)value))) failures++;
                        foreach (var offset in new[] { 0, 1, 7, 31, 32, 33, 63 })
                        {
                            if (BitOperations.RotateLeft(value, offset) != (wide ? (nuint)BitOperations.RotateLeft((ulong)value, offset) : (nuint)BitOperations.RotateLeft((uint)value, offset))) failures++;
                            if (BitOperations.RotateRight(value, offset) != (wide ? (nuint)BitOperations.RotateRight((ulong)value, offset) : (nuint)BitOperations.RotateRight((uint)value, offset))) failures++;
                        }
                    }
                    // A native word is 32 or 64 bits wide.
                    if (BitOperations.LeadingZeroCount((nuint)1) != IntPtr.Size * 8 - 1) failures++;
                    return failures;
                }

                private static int Endianness()
                {
                    var failures = 0;
                    var wide = IntPtr.Size == 8;
                    var unsigned = new nuint[Samples.Length];
                    var signed = new nint[Samples.Length];
                    for (var index = 0; index < Samples.Length; index++)
                    {
                        var value = (nuint)Samples[index];
                        unsigned[index] = value;
                        signed[index] = (nint)value;
                        if (BinaryPrimitives.ReverseEndianness(value) != (wide ? (nuint)BinaryPrimitives.ReverseEndianness((ulong)value) : (nuint)BinaryPrimitives.ReverseEndianness((uint)value))) failures++;
                        if (BinaryPrimitives.ReverseEndianness((nint)value) != (wide ? (nint)BinaryPrimitives.ReverseEndianness((long)value) : (nint)BinaryPrimitives.ReverseEndianness((int)value))) failures++;
                    }
                    var reversedUnsigned = new nuint[unsigned.Length];
                    var reversedSigned = new nint[signed.Length];
                    BinaryPrimitives.ReverseEndianness(unsigned, reversedUnsigned);
                    BinaryPrimitives.ReverseEndianness(signed, reversedSigned);
                    for (var index = 0; index < unsigned.Length; index++)
                    {
                        if (reversedUnsigned[index] != BinaryPrimitives.ReverseEndianness(unsigned[index])) failures++;
                        if (reversedSigned[index] != BinaryPrimitives.ReverseEndianness(signed[index])) failures++;
                    }
                    return failures;
                }

                // Saturation clamps to the native type's range of its width.
                private static int Saturation()
                {
                    var failures = 0;
                    var wide = IntPtr.Size == 8;
                    foreach (var value in new long[] { long.MinValue, -1, 0, 1, int.MaxValue, uint.MaxValue, (long)uint.MaxValue + 1, long.MaxValue })
                    {
                        var expected = value <= 0 ? 0UL : wide ? (ulong)value : Math.Min((ulong)value, uint.MaxValue);
                        if ((ulong)nuint.CreateSaturating(value) != expected) failures++;
                    }
                    foreach (var value in new uint[] { 0, 1, int.MaxValue, (uint)int.MaxValue + 1, uint.MaxValue })
                    {
                        var expected = wide ? (long)value : Math.Min((long)value, int.MaxValue);
                        if ((long)nint.CreateSaturating(value) != expected) failures++;
                    }
                    foreach (var value in new UInt128[] { 0, int.MaxValue, (UInt128)int.MaxValue + 1, long.MaxValue, (UInt128)long.MaxValue + 1, UInt128.MaxValue })
                    {
                        var limit = wide ? (UInt128)long.MaxValue : int.MaxValue;
                        var expected = value >= limit ? (long)limit : (long)value;
                        if ((long)nint.CreateSaturating(value) != expected) failures++;
                    }
                    return failures;
                }

                public static int Trace() => 0;
            }
            """;
}
