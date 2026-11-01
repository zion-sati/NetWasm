namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ArrayCopyBoxingCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ArrayCopyBoxesAndUnboxesAcrossCompilerProfilesAndTargets() =>
        runner.Run(CreateFixture());

    [Fact]
    public void ProductionRuntimeBoxesAndUnboxesArrayElements() =>
        runner.Run(CreateFixture() with
        {
            Matrix = new(
                CorpusMatrixProfile.Extended,
                "Release-Wasm32-Direct-Linked"),
        });

    private static CorpusFixture CreateFixture() =>
        new(
            "ArrayCopyBoxingCompatibilityCorpus",
            "NetWasm.Correctness.ArrayCopyBoxing",
            """
            using System;

            namespace NetWasm.Correctness.ArrayCopyBoxing;

            public struct Payload
            {
                public object? Reference;
                public int Value;
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => BoxIntegers(),
                    1 => UnboxIntegers(),
                    2 => BoxIntoInterfaceArray(),
                    3 => UnboxFromInterfaceArray(),
                    4 => RejectWrongBoxedValue(),
                    5 => RejectNullUnbox(),
                    6 => RejectIncompatibleArrayTypes(),
                    7 => CopyStructWithReference(),
                    8 => PreserveReferenceOverlap(),
                    _ => PreserveValueOverlap(),
                };

                private static int BoxIntegers()
                {
                    Array source = new[] { 2, 4, 6 };
                    var destination = new object[5];
                    Array.Copy(source, 0, destination, 1, 3);
                    return (int)destination[1] * 100 + (int)destination[2] * 10 + (int)destination[3];
                }

                private static int UnboxIntegers()
                {
                    Array source = new object[] { 3, 5, 7 };
                    var destination = new int[3];
                    Array.Copy(source, destination, 3);
                    return Checksum(destination);
                }

                private static int BoxIntoInterfaceArray()
                {
                    Array source = new[] { 1, 2, 3 };
                    var destination = new IComparable[3];
                    Array.Copy(source, destination, 3);
                    return destination[0].CompareTo(1) * 100
                        + destination[1].CompareTo(2) * 10
                        + destination[2].CompareTo(3);
                }

                private static int UnboxFromInterfaceArray()
                {
                    Array source = new IComparable[] { 4, 5, 6 };
                    var destination = new int[3];
                    Array.Copy(source, destination, 3);
                    return Checksum(destination);
                }

                private static int RejectWrongBoxedValue()
                {
                    Array source = new object[] { 1, "bad", 3 };
                    var destination = new int[3];
                    try
                    {
                        Array.Copy(source, destination, 3);
                        return -1;
                    }
                    catch (InvalidCastException)
                    {
                        return destination[0] * 10 + destination[1];
                    }
                }

                private static int RejectNullUnbox()
                {
                    Array source = new object?[] { 1, null, 3 };
                    try
                    {
                        Array.Copy(source, new int[3], 3);
                        return -1;
                    }
                    catch (InvalidCastException)
                    {
                        return 1;
                    }
                }

                private static int RejectIncompatibleArrayTypes()
                {
                    try
                    {
                        Array.Copy(new[] { 1, 2 }, new string[2], 2);
                        return -1;
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        return 1;
                    }
                }

                private static int CopyStructWithReference()
                {
                    var marker = new object();
                    Array source = new[] { new Payload { Reference = marker, Value = 42 } };
                    var boxed = new object[1];
                    Array.Copy(source, boxed, 1);
                    var destination = new Payload[1];
                    Array.Copy(boxed, destination, 1);
                    return ReferenceEquals(marker, destination[0].Reference)
                        ? destination[0].Value
                        : -1;
                }

                private static int PreserveReferenceOverlap()
                {
                    var values = new[] { "a", "b", "c", "d" };
                    Array.Copy(values, 0, values, 1, 3);
                    return values[0].Length * 1000 + values[1].Length * 100
                        + values[2].Length * 10 + values[3].Length;
                }

                private static int PreserveValueOverlap()
                {
                    var values = new[] { 1, 2, 3, 4 };
                    Array.Copy(values, 0, values, 1, 3);
                    return Checksum(values);
                }

                private static int Checksum(int[] values)
                {
                    int result = 0;
                    foreach (int value in values) result = result * 11 + value;
                    return result;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        };
}
