namespace NetWasm.Compiler.Tests.Correctness;

// Primitive hash codes are deterministic in .NET and decide hashed collections' layout, so each
// primitive's matches desktop's, char's upper half included.
[Collection(CorrectnessTestGroup.Name)]
public sealed class PrimitiveHashCodeCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void PrimitiveHashCodesMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "PrimitiveHashCodeCorpus",
            "NetWasm.Correctness.PrimitiveHashCodes",
            """
            using System;

            namespace NetWasm.Correctness.PrimitiveHashCodes;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Mix('a'.GetHashCode(), '￿'.GetHashCode(), '\0'.GetHashCode()),
                    1 => Mix(true.GetHashCode(), ((byte)200).GetHashCode(), ((sbyte)-5).GetHashCode()),
                    2 => Mix(((short)-300).GetHashCode(), ((ushort)60000).GetHashCode(), (-7).GetHashCode()),
                    3 => Mix(uint.MaxValue.GetHashCode(), (-1234567890123L).GetHashCode(), ulong.MaxValue.GetHashCode()),
                    4 => Mix(1.5f.GetHashCode(), (-0f).GetHashCode(), float.NaN.GetHashCode()),
                    5 => Mix(2.25.GetHashCode(), (-0.0).GetHashCode(), double.NaN.GetHashCode()),
                    _ => Mix(((Half)1.5f).GetHashCode(), new Int128(1, 2).GetHashCode() == new Int128(1, 2).GetHashCode() ? 1 : 0, 0),
                };

                private static int Mix(int a, int b, int c) => unchecked((a * 31 + b) * 31 + c);

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
