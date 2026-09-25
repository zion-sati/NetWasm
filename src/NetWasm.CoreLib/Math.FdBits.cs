namespace System;

public static partial class Math
{
    // Endianness-independent equivalents of fdlibm's word-access macros.
    private static int FdHigh(double value) => (int)(BitConverter.DoubleToUInt64Bits(value) >> 32);
    private static int FdLow(double value) => (int)BitConverter.DoubleToUInt64Bits(value);
    private static double FdWithHigh(double value, int high) =>
        BitConverter.UInt64BitsToDouble(((ulong)(uint)high << 32) |
            (BitConverter.DoubleToUInt64Bits(value) & 0xFFFFFFFFUL));
    private static double FdWithLow(double value, int low) =>
        BitConverter.UInt64BitsToDouble((BitConverter.DoubleToUInt64Bits(value) & 0xFFFFFFFF00000000UL) |
            (uint)low);
}
