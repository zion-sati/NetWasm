namespace NetWasm.Compiler.Tests.Correctness;

public sealed class FloatingCopySignTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CopySignPreservesMagnitudePayloadAndReplacesOnlyTheSign() =>
        runner.Run(new(
            "FloatingCopySign",
            "NetWasm.Correctness.FloatingCopySign",
            """
            using System;
            using System.Numerics;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.FloatingCopySign;

            public static class EntryPoint
            {
                public static int Run(int input) => input == 0 ? Singles() : Doubles();

                private static int Singles()
                {
                    uint[] values = [0, 0x80000000, 1, 0x807fffff, 0x00800000, 0x3f800000,
                        0x7f7fffff, 0xff7fffff, 0x7f800000, 0xff800000, 0x7fc12345, 0xffc12345,
                        0x7f812345, 0xff812345];
                    var count = 0;
                    foreach (var x in values)
                    foreach (var y in values)
                    {
                        var value = BitConverter.UInt32BitsToSingle(x);
                        var sign = BitConverter.UInt32BitsToSingle(y);
                        var expected = (x & 0x7fffffff) | (y & 0x80000000);
                        if (BitConverter.SingleToUInt32Bits(CopySingle(value, sign)) != expected ||
                            BitConverter.SingleToUInt32Bits(float.CopySign(value, sign)) != expected ||
                            BitConverter.SingleToUInt32Bits(CopyGeneric(value, sign)) != expected)
                            throw new InvalidOperationException("Single CopySign changed magnitude bits.");
                        count++;
                    }
                    return count;
                }

                private static int Doubles()
                {
                    ulong[] values = [0, 0x8000000000000000, 1, 0x800fffffffffffff,
                        0x0010000000000000, 0x3ff0000000000000, 0x7fefffffffffffff,
                        0xffefffffffffffff, 0x7ff0000000000000, 0xfff0000000000000,
                        0x7ff8123456789abc, 0xfff8123456789abc, 0x7ff0123456789abc,
                        0xfff0123456789abc];
                    var count = 0;
                    foreach (var x in values)
                    foreach (var y in values)
                    {
                        var value = BitConverter.UInt64BitsToDouble(x);
                        var sign = BitConverter.UInt64BitsToDouble(y);
                        var expected = (x & 0x7fffffffffffffff) | (y & 0x8000000000000000);
                        if (BitConverter.DoubleToUInt64Bits(CopyDouble(value, sign)) != expected ||
                            BitConverter.DoubleToUInt64Bits(double.CopySign(value, sign)) != expected ||
                            BitConverter.DoubleToUInt64Bits(CopyGeneric(value, sign)) != expected)
                            throw new InvalidOperationException("Double CopySign changed magnitude bits.");
                        count++;
                    }
                    return count;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static float CopySingle(float value, float sign) => MathF.CopySign(value, sign);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double CopyDouble(double value, double sign) => Math.CopySign(value, sign);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T CopyGeneric<T>(T value, T sign) where T : INumber<T> => T.CopySign(value, sign);

                public static int Trace() => 0;
            }
            """,
            [0, 1])
        {
            ExpectedReturnValue = 196,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
