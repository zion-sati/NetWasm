namespace NetWasm.Compiler.Tests.Correctness;

public sealed class BitArrayCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void BitArrayMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "BitArrayCompatibilityCorpus",
            "NetWasm.Correctness.BitArrays",
            """
            using System;
            using System.Collections;
            using System.Runtime.InteropServices;

            namespace NetWasm.Correctness.BitArrays;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => LengthConstructors(),
                    1 => BooleanConstructors(),
                    2 => ByteConstructors(),
                    3 => Int32Constructors(),
                    4 => GetSet(),
                    5 => SetAllAndMasks(),
                    6 => BinaryOperations(),
                    7 => Shifts(),
                    8 => Resize(),
                    9 => Copies(),
                    10 => Queries(),
                    11 => Clone(),
                    12 => EnumeratorAndClone(),
                    13 => ZeroShiftInvalidation(),
                    14 => SameLengthInvalidation(),
                    15 => MarshalBytes(),
                    _ => Failures(),
                };

                private static int LengthConstructors()
                {
                    var clear = new BitArray(35);
                    var set = new BitArray(35, true);
                    return clear.Count == 35 && clear.Length == 35 && !clear.HasAnySet()
                        && set.HasAllSet() && CountBits(set) == 35
                        ? Checksum(set)
                        : -1;
                }

                private static int BooleanConstructors()
                {
                    bool[] source = [true, false, true, true, false, false, true, false, true];
                    var array = new BitArray(source);
                    var copy = new BitArray(source);
                    source[0] = false;
                    return Checksum(array) * 31 + Checksum(copy);
                }

                private static int ByteConstructors()
                {
                    byte[] source = [0xA5, 0x03];
                    var array = new BitArray(source);
                    var copy = new BitArray(source);
                    source[0] = 0;
                    return array.Length == 16 && copy.Length == 16
                        ? Checksum(array) * 31 + Checksum(copy)
                        : -1;
                }

                private static int Int32Constructors()
                {
                    int[] source = [unchecked((int)0x80000001), 0x55AA55AA];
                    var array = new BitArray(source);
                    var copy = new BitArray(source);
                    source[0] = 0;
                    return array.Length == 64 && array[0] && array[31] && copy[33]
                        ? Checksum(array) * 31 + Checksum(copy)
                        : -1;
                }

                private static int GetSet()
                {
                    var bits = new BitArray(40);
                    bits.Set(0, true);
                    bits[7] = true;
                    bits[8] = true;
                    bits.Set(31, true);
                    bits[39] = true;
                    bits.Set(8, false);
                    return bits.Get(0) && bits[7] && !bits[8] && bits[31] && bits[39]
                        ? Checksum(bits)
                        : -1;
                }

                private static int SetAllAndMasks()
                {
                    var bits = new BitArray(35);
                    bits.SetAll(true);
                    var bytes = new byte[5];
                    var ints = new int[2];
                    bits.CopyTo(bytes, 0);
                    bits.CopyTo(ints, 0);
                    int set = bytes[4] * 100 + ints[1];
                    bits.SetAll(false);
                    return !bits.HasAnySet() ? set + CountBits(bits) : -1;
                }

                private static int BinaryOperations()
                {
                    bool[] leftValues = [true, true, false, false, true, false, true, false, true];
                    bool[] rightValues = [true, false, true, false, true, true, false, false, true];
                    var and = new BitArray(leftValues);
                    var or = new BitArray(leftValues);
                    var xor = new BitArray(leftValues);
                    var not = new BitArray(leftValues);
                    bool same = ReferenceEquals(and, and.And(new BitArray(rightValues)))
                        && ReferenceEquals(or, or.Or(new BitArray(rightValues)))
                        && ReferenceEquals(xor, xor.Xor(new BitArray(rightValues)))
                        && ReferenceEquals(not, not.Not());
                    return same
                        ? Checksum(and) * 31 * 31 * 31 + Checksum(or) * 31 * 31
                            + Checksum(xor) * 31 + Checksum(not)
                        : -1;
                }

                private static int Shifts()
                {
                    var source = new BitArray(new byte[] { 0xA5, 0x13 });
                    source.Length = 13;
                    var left1 = new BitArray(source).LeftShift(1);
                    var left8 = new BitArray(source).LeftShift(8);
                    var left32 = new BitArray(source).LeftShift(32);
                    var right1 = new BitArray(source).RightShift(1);
                    var right8 = new BitArray(source).RightShift(8);
                    var right32 = new BitArray(source).RightShift(32);
                    return Checksum(left1) * 31 * 31 * 31 * 31 * 31
                        + Checksum(left8) * 31 * 31 * 31 * 31
                        + Checksum(left32) * 31 * 31 * 31
                        + Checksum(right1) * 31 * 31
                        + Checksum(right8) * 31
                        + Checksum(right32);
                }

                private static int Resize()
                {
                    var bits = new BitArray(33, true);
                    bits.Length = 5;
                    int shortChecksum = Checksum(bits);
                    bits.Length = 37;
                    bool grownBitsClear = true;
                    for (int index = 5; index < bits.Length; index++) grownBitsClear &= !bits[index];
                    bits[36] = true;
                    bits.Length = 3;
                    bits.Length = 37;
                    return grownBitsClear && !bits[36]
                        ? shortChecksum * 100 + Checksum(bits)
                        : -1;
                }

                private static int Copies()
                {
                    var bits = new BitArray(new bool[]
                    {
                        true, false, true, true, false, false, true, false, true, true,
                    });
                    var bools = new bool[13];
                    var bytes = new byte[4];
                    var ints = new int[3];
                    bits.CopyTo(bools, 2);
                    bits.CopyTo(bytes, 1);
                    bits.CopyTo(ints, 1);
                    return (bools[2] ? 100000 : 0) + (bools[4] ? 10000 : 0)
                        + bytes[1] * 100 + bytes[2] * 10 + ints[1];
                }

                private static int Queries()
                {
                    var empty = new BitArray(0);
                    var partial = new BitArray(13);
                    partial[0] = true;
                    partial[7] = true;
                    partial[12] = true;
                    var full = new BitArray(13, true);
                    return empty.HasAllSet() && !empty.HasAnySet() && CountBits(empty) == 0
                        && !partial.HasAllSet() && partial.HasAnySet() && CountBits(partial) == 3
                        && full.HasAllSet() && full.HasAnySet() && CountBits(full) == 13
                        ? 313
                        : -1;
                }

                private static int Clone()
                {
                    var original = new BitArray(new bool[] { true, false, true, false, true });
                    var clone = (BitArray)original.Clone();
                    clone[0] = false;
                    clone.Length = 8;
                    clone[7] = true;
                    return original.Length == 5 && original[0] && clone.Length == 8 && !clone[0]
                        ? Checksum(original) * 100 + Checksum(clone)
                        : -1;
                }

                private static int EnumeratorAndClone()
                {
                    var bits = new BitArray(new bool[] { true, false, true });
                    IEnumerator first = bits.GetEnumerator();
                    first.MoveNext();
                    object firstTrue = first.Current;
                    var clone = (IEnumerator)((ICloneable)first).Clone();
                    first.MoveNext();
                    clone.MoveNext();
                    object firstFalse = first.Current;
                    object cloneFalse = clone.Current;
                    first.MoveNext();
                    object secondTrue = first.Current;
                    return ReferenceEquals(firstTrue, secondTrue)
                        && ReferenceEquals(firstFalse, cloneFalse)
                        && !(bool)firstFalse
                        ? ((bool)firstTrue ? 100 : 0) + ((bool)secondTrue ? 10 : 0)
                        : -1;
                }

                private static int ZeroShiftInvalidation()
                {
                    var left = new BitArray(3, true);
                    IEnumerator leftEnumerator = left.GetEnumerator();
                    left.LeftShift(0);
                    int score = Invalidated(leftEnumerator) ? 1 : 0;

                    var right = new BitArray(3, true);
                    IEnumerator rightEnumerator = right.GetEnumerator();
                    right.RightShift(0);
                    return score + (Invalidated(rightEnumerator) ? 10 : 0);
                }

                private static int SameLengthInvalidation()
                {
                    var bits = new BitArray(3, true);
                    IEnumerator enumerator = bits.GetEnumerator();
                    bits.Length = bits.Length;
                    return Invalidated(enumerator) ? Checksum(bits) : -1;
                }

                private static int MarshalBytes()
                {
                    var bits = new BitArray(10);
                    Span<byte> bytes = CollectionsMarshal.AsBytes(bits);
                    bytes[0] = 0x81;
                    bytes[1] = 0x03;
                    return bytes.Length == 2 && bits[0] && bits[7] && bits[8] && bits[9]
                        ? Checksum(bits)
                        : -1;
                }

                private static int Failures()
                {
                    int score = 0;
                    try { _ = new BitArray(-1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "length" ? 1 : 0; }
                    var bits = new BitArray(3);
                    try { _ = bits[-1]; }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 10 : 0; }
                    try { bits.LeftShift(-1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "count" ? 100 : 0; }
                    try { bits.And(new BitArray(4)); }
                    catch (ArgumentException) { score += 1000; }
                    try { bits.CopyTo(new long[1], 0); }
                    catch (ArgumentException exception) { score += exception.ParamName == "array" ? 10000 : 0; }
                    try { bits.CopyTo(new bool[2], 0); }
                    catch (ArgumentException) { score += 100000; }
                    return score;
                }

                private static bool Invalidated(IEnumerator enumerator)
                {
                    try
                    {
                        enumerator.MoveNext();
                        return false;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }

                private static int Checksum(BitArray bits)
                {
                    int result = 0;
                    for (int index = 0; index < bits.Length; index++)
                    {
                        result = unchecked(result * 31 + (bits[index] ? 1 : 0));
                    }
                    return result;
                }

                private static int CountBits(BitArray bits)
                {
                    int count = 0;
                    for (int index = 0; index < bits.Length; index++)
                    {
                        if (bits[index]) count++;
                    }
                    return count;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
