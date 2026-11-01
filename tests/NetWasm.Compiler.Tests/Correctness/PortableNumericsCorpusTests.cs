namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PortableNumericsCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void PortableNumericsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "PortableNumericsCorpus",
            "NetWasm.Correctness.PortableNumerics",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            namespace NetWasm.Correctness.PortableNumerics;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => (int)System.Numerics.Vector2.Dot(
                        new System.Numerics.Vector2(1, 2),
                        new System.Numerics.Vector2(3, 4)),
                    1 => (int)System.Numerics.Vector3.Cross(
                        new System.Numerics.Vector3(1, 0, 0),
                        new System.Numerics.Vector3(0, 1, 0)).Z,
                    2 => (int)System.Numerics.Vector4.Lerp(
                        new System.Numerics.Vector4(0),
                        new System.Numerics.Vector4(8),
                        0.25f).X,
                    3 => TransformChecksum(),
                    4 => System.Numerics.Quaternion.Identity.IsIdentity &&
                        System.Numerics.Quaternion.Zero.W == 0 ? 1 : 0,
                    5 => (int)System.Numerics.Plane.DotCoordinate(
                        new System.Numerics.Plane(0, 1, 0, -2),
                        new System.Numerics.Vector3(0, 5, 0)),
                    6 => (new System.Numerics.Vector<int>(3) +
                        new System.Numerics.Vector<int>(4))[0],
                    7 => (int)System.Numerics.BitOperations.PopCount(0xF0F0u) +
                        (int)System.Numerics.BitOperations.RotateLeft(1u, 4),
                    8 => new System.Numerics.TotalOrderIeee754Comparer<float>()
                        .Compare(-0.0f, 0.0f) < 0 ? 1 : 0,
                    9 => float.IsNaN(System.Numerics.Vector2.Normalize(
                        System.Numerics.Vector2.Zero).X) ? 1 : 0,
                    10 => System.Numerics.Matrix3x2.Invert(
                        new System.Numerics.Matrix3x2(1, 2, 2, 4, 0, 0),
                        out _) ? 0 : 1,
                    11 => (int)(System.Numerics.BigInteger.Parse(
                        "18446744073709551615") + 1).ToByteArray().Length,
                    // The vector width itself follows the host: 32 bytes with AVX2.
                    12 => System.Numerics.Vector<nint>.Count * System.IntPtr.Size ==
                        System.Numerics.Vector<byte>.Count ? 1 : 0,
                    13 => System.Numerics.BitOperations.Log2(0u) +
                        System.Numerics.BitOperations.Log2(1u) +
                        System.Numerics.BitOperations.Log2(2u) +
                        System.Numerics.BitOperations.Log2(3u) +
                        System.Numerics.BitOperations.Log2(uint.MaxValue),
                    14 => System.Numerics.BitOperations.Log2(0ul) +
                        System.Numerics.BitOperations.Log2(1ul) +
                        System.Numerics.BitOperations.Log2(2ul) +
                        System.Numerics.BitOperations.Log2(3ul) +
                        System.Numerics.BitOperations.Log2(ulong.MaxValue),
                    15 => BitOperationChecksum(),
                    16 => PrimitiveBitOperationChecksum(),
                    17 => NativeBitOperationChecksum(),
                    18 => ScalarBitBoundaryChecksum(),
                    19 => Crc32CChecksum(),
                    _ => ComplexChecksum(),
                };

                private static int BitOperationChecksum() =>
                    System.Numerics.BitOperations.LeadingZeroCount(0u) == 32 &&
                    System.Numerics.BitOperations.LeadingZeroCount(1ul) == 63 &&
                    System.Numerics.BitOperations.PopCount(0xF0F0F0F0F0F0F0F0ul) == 32 &&
                    System.Numerics.BitOperations.TrailingZeroCount(0u) == 32 &&
                    System.Numerics.BitOperations.TrailingZeroCount(1ul << 63) == 63 &&
                    System.Numerics.BitOperations.RotateLeft(0x12345678u, 8) == 0x34567812u &&
                    System.Numerics.BitOperations.RotateLeft(1u, -1) == 0x80000000u &&
                    System.Numerics.BitOperations.RotateRight(1ul, -1) == 2ul &&
                    System.Numerics.BitOperations.RotateRight(
                        0x0123456789ABCDEFul, 8) == 0xEF0123456789ABCDul ? 1 : 0;

                private static int PrimitiveBitOperationChecksum() =>
                    (long.TrailingZeroCount(0) == 64 ? 1 : 0) |
                    (ulong.TrailingZeroCount(0) == 64 ? 2 : 0) |
                    (long.RotateLeft(1, 1) == 2 ? 4 : 0) |
                    (ulong.RotateRight(2, 1) == 1 ? 8 : 0) |
                    (int.PopCount(-1) == 32 ? 16 : 0) |
                    (uint.LeadingZeroCount(1) == 31 ? 32 : 0);

                private static int NativeBitOperationChecksum()
                {
                    var bitCount = nuint.Size * 8;
                    return System.Numerics.BitOperations.LeadingZeroCount((nuint)1) ==
                            bitCount - 1 &&
                        System.Numerics.BitOperations.TrailingZeroCount((nuint)0) ==
                            bitCount &&
                        System.Numerics.BitOperations.RotateLeft(
                            (nuint)1, bitCount - 1) == (nuint)1 << (bitCount - 1)
                        ? 1 : 0;
                }

                private static int ScalarBitBoundaryChecksum() =>
                    System.Numerics.BitOperations.LeadingZeroCount(0u) == 32 &&
                    System.Numerics.BitOperations.LeadingZeroCount(0ul) == 64 &&
                    System.Numerics.BitOperations.LeadingZeroCount(1u << 31) == 0 &&
                    System.Numerics.BitOperations.LeadingZeroCount(1ul << 32) == 31 &&
                    System.Numerics.BitOperations.TrailingZeroCount(0u) == 32 &&
                    System.Numerics.BitOperations.TrailingZeroCount(0ul) == 64 &&
                    System.Numerics.BitOperations.TrailingZeroCount(1u << 31) == 31 &&
                    System.Numerics.BitOperations.TrailingZeroCount(1ul << 32) == 32 &&
                    System.Numerics.BitOperations.PopCount(uint.MaxValue) == 32 &&
                    System.Numerics.BitOperations.PopCount(ulong.MaxValue) == 64 &&
                    System.Numerics.BitOperations.Log2(0u) == 0 &&
                    System.Numerics.BitOperations.Log2(uint.MaxValue) == 31 &&
                    System.Numerics.BitOperations.Log2(1ul << 32) == 32 ? 1 : 0;

                private static int Crc32CChecksum() =>
                    System.Numerics.BitOperations.Crc32C(0x12345678u, (byte)0xA5) == 0x6CE9992Eu &&
                    System.Numerics.BitOperations.Crc32C(0x12345678u, (ushort)0xBEEF) == 0xD78220DCu &&
                    System.Numerics.BitOperations.Crc32C(0x12345678u, 0xDEADBEEFu) == 0xF3ED4B20u &&
                    System.Numerics.BitOperations.Crc32C(0x12345678u, 0x0123456789ABCDEFul) == 0xA3D207BEu
                        ? 1 : 0;

                private static int TransformChecksum()
                {
                    var matrix = System.Numerics.Matrix3x2.CreateTranslation(3, 4);
                    var transformed = System.Numerics.Vector2.Transform(
                        new System.Numerics.Vector2(1, 2), matrix);
                    return ((int)transformed.X * 10) + (int)transformed.Y;
                }

                private static int ComplexChecksum()
                {
                    var value = new System.Numerics.Complex(1, 2) *
                        new System.Numerics.Complex(3, 4);
                    return (int)(value.Real + value.Imaginary);
                }

                public static int Trace() => 0;
            }
            """;
}
