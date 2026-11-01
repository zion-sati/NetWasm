namespace NetWasm.Compiler.Tests.Correctness;

public sealed class NarrowIntegerBitOperationTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(0, 16)]
    [InlineData(1, 1)]
    [InlineData(2, 16384)]
    [InlineData(3, 42)]
    public void SixteenBitOperationsDoNotObserveSignExtendedStorage(int input, int expected) =>
        runner.Run(new(
            "NarrowIntegerBits" + input,
            "NetWasm.Correctness.NarrowIntegerBits",
            """
            using System;
            using System.Numerics;

            namespace NetWasm.Correctness.NarrowIntegerBits;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => short.PopCount(-1),
                    1 => short.RotateLeft(short.MinValue, 1),
                    2 => short.RotateRight(short.MinValue, 1),
                    _ => FullSurface(),
                };

                private static int FullSurface()
                {
                    ushort[] values = [0, 1, 2, 0x7fff, 0x8000, 0x8001, 0xaaaa, 0x5555, 0xfffe, 0xffff];
                    int[] amounts = [int.MinValue, -33, -17, -16, -1, 0, 1, 15, 16, 17, 33, int.MaxValue];
                    foreach (var value in values)
                    {
                        var leading = 16;
                        var trailing = 16;
                        var population = 0;
                        for (var bit = 0; bit < 16; bit++)
                        {
                            if ((value & (1 << bit)) == 0) continue;
                            population++;
                            leading = 15 - bit;
                            if (trailing == 16) trailing = bit;
                        }
                        var signed = unchecked((short)value);
                        if (!Counts(signed, (short)leading, (short)population, (short)trailing) ||
                            !Counts(value, (ushort)leading, (ushort)population, (ushort)trailing) ||
                            !Counts((char)value, (char)leading, (char)population, (char)trailing))
                            return -1;
                        foreach (var amount in amounts)
                        {
                            var shift = amount & 15;
                            var left = (ushort)(((uint)value << shift) | ((uint)value >> ((16 - shift) & 15)));
                            var right = (ushort)(((uint)value >> shift) | ((uint)value << ((16 - shift) & 15)));
                            if (!Rotations(signed, amount, unchecked((short)left), unchecked((short)right)) ||
                                !Rotations(value, amount, left, right) ||
                                !Rotations((char)value, amount, (char)left, (char)right))
                                return -2;
                        }
                    }
                    return 42;
                }

                private static bool Counts<T>(T value, T leading, T population, T trailing)
                    where T : IBinaryInteger<T> =>
                    T.LeadingZeroCount(value) == leading && T.PopCount(value) == population &&
                    T.TrailingZeroCount(value) == trailing;

                private static bool Rotations<T>(T value, int amount, T left, T right)
                    where T : IBinaryInteger<T> =>
                    T.RotateLeft(value, amount) == left && T.RotateRight(value, amount) == right;

                public static int Trace() => 0;
            }
            """,
            [input])
        {
            ExpectedReturnValue = expected,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
