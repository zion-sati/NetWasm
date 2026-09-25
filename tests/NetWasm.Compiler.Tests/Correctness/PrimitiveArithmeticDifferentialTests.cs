namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class PrimitiveArithmeticDifferentialTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DebugAndReleasePrimitiveArithmeticMatchesDesktopDotNet()
    {
        runner.Run(new(
            "PrimitiveArithmeticDifferential",
            "NetWasm.Correctness.Primitives",
            """
            namespace NetWasm.Correctness.Primitives;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    var boolean = input != 0;
                    char character = (char)('A' + (input & 7));
                    sbyte signedByte = (sbyte)input;
                    byte unsignedByte = (byte)(input + 8);
                    short signedShort = (short)(input * 2);
                    ushort unsignedShort = (ushort)(input + 16);
                    var signed = input;
                    var unsigned = unchecked((uint)input);
                    long signedLong = input;
                    ulong unsignedLong = unchecked((ulong)(long)input);
                    nint nativeSigned = input;
                    nuint nativeUnsigned = unchecked((nuint)(uint)input);
                    float single = input + 0.5f;
                    double @double = input + 0.25d;
                    decimal money = input + 0.5m;

                    var total = boolean ? 1 : 0;
                    total += character - 'A';
                    total += signedByte + unsignedByte;
                    total += signedShort + unsignedShort;
                    total += checked(signed + 10);
                    total += unchecked((int)(unsigned + 3U));
                    total += (int)((signedLong * 3L) / 2L);
                    total += (int)((unsignedLong & 31UL) ^ 7UL);
                    total += (int)(nativeSigned + 2);
                    total += (int)(nativeUnsigned & 15);
                    total += (int)((single * 2f) % 17f);
                    total += (int)((@double / 2d) % 19d);
                    total += (int)(money * 2m);
                    total += (input << 2) >> 1;
                    total += input < 0 ? ~input : input & 7;
                    _trace = total;
                    return total;
                }

                public static int Trace() => _trace;
            }
            """,
            [-3, 0, 3, 41]));
    }
}
