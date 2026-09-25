using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class PrimitiveCompilationTests
{
    [Fact]
    public void CompilerExecutesInvariantIntegerContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "PrimitiveIntegerFixture",
            """
            using System;
            using System.Globalization;

            namespace PrimitiveIntegerFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (int.Parse(" -2147483648 ") != int.MinValue) return 1;
                    if (uint.Parse("4294967295") != uint.MaxValue) return 2;
                    if (long.Parse("-9223372036854775808") != long.MinValue) return 3;
                    if (ulong.Parse("18446744073709551615") != ulong.MaxValue) return 4;
                    if (short.Parse("-32768") != short.MinValue) return 5;
                    if (ushort.Parse("65535") != ushort.MaxValue) return 6;
                    if (sbyte.Parse("-128") != sbyte.MinValue) return 7;
                    if (byte.Parse("255") != byte.MaxValue) return 8;
                    if (int.MinValue.ToString() != "-2147483648") return 9;
                    if (uint.MaxValue.ToString() != "4294967295") return 10;
                    if (long.MinValue.ToString() != "-9223372036854775808") return 11;
                    if (ulong.MaxValue.ToString() != "18446744073709551615") return 12;
                    if ((-1).ToString("X") != "FFFFFFFF") return 13;
                    if (((ushort)0xabcd).ToString("x") != "abcd") return 14;
                    if (int.Parse("FFFFFFFF", NumberStyles.HexNumber) != -1) return 22;
                    if (!uint.TryParse("abcdef", NumberStyles.HexNumber, out var hex) ||
                        hex != 0xabcdefU) return 23;
                    if (!int.TryParse("+42", out var parsed) || parsed != 42) return 15;
                    if (int.TryParse("2147483648", out parsed) || parsed != 0) return 16;
                    if (int.TryParse("1x", out parsed) || parsed != 0) return 17;
                    if (true.CompareTo(false) <= 0 || false.CompareTo(false) != 0) return 18;
                    if ('z'.CompareTo('a') <= 0 || char.Parse("x") != 'x') return 19;
                    if (!bool.Parse(" \tTrUe\r\n") || bool.TryParse("not-bool", out _)) return 24;
                    if (((IComparable)42).CompareTo(41) <= 0) return 25;
                    if (((IComparable)(uint)42).CompareTo((uint)41) <= 0) return 26;
                    if (((IComparable)'b').CompareTo('a') <= 0) return 27;
                    if (((IComparable)false).CompareTo(null) != 1) return 28;
                    try { _ = ((IComparable)42).CompareTo((short)42); return 29; }
                    catch (ArgumentException) { }
                    try { _ = int.Parse("2147483648"); return 20; }
                    catch (OverflowException) { }
                    try { _ = int.Parse("nope"); return 21; }
                    catch (FormatException) { }
                    if (checked((uint)40 + 2) != 42) return 30;
                    if (checked((uint)44 - 2) != 42) return 31;
                    try { _ = checked(uint.MaxValue + (uint)input); return 32; }
                    catch (OverflowException) { }
                    try { _ = checked((uint)0 - (uint)input); return 33; }
                    catch (OverflowException) { }
                    return input - input;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "PrimitiveIntegerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesFloatingPointAndMathContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "FloatingMathFixture",
            """
            using System;

            namespace FloatingMathFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var alternateNaN = BitConverter.Int64BitsToDouble(
                        unchecked((long)0xfff8000000000042UL));
                    if (!double.IsNaN(alternateNaN) || !double.NaN.Equals(alternateNaN)) return 1;
                    if (double.NaN.GetHashCode() != alternateNaN.GetHashCode()) return 2;
                    if (0.0.GetHashCode() != (-0.0).GetHashCode()) return 3;
                    if (!double.IsNegative(-0.0) || double.IsNegative(0.0)) return 4;
                    if (BitConverter.DoubleToInt64Bits(
                        BitConverter.Int64BitsToDouble(unchecked((long)0x8123456789abcdefUL))) !=
                        unchecked((long)0x8123456789abcdefUL)) return 5;
                    if (BitConverter.SingleToInt32Bits(
                        BitConverter.Int32BitsToSingle(unchecked((int)0x81234567U))) !=
                        unchecked((int)0x81234567U)) return 6;
                    if (Math.Floor(-1.25) != -2.0 || Math.Ceiling(-1.25) != -1.0) return 7;
                    if (Math.Truncate(-1.75) != -1.0 || Math.Round(2.5) != 2.0 ||
                        Math.Round(3.5) != 4.0) return 8;
                    if (Math.Sqrt(81.0) != 9.0 || MathF.Sqrt(16.0f) != 4.0f) return 9;
                    if (!double.IsNaN(Math.Sqrt(-1.0))) return 10;
                    if (BitConverter.DoubleToInt64Bits(Math.Min(0.0, -0.0)) !=
                        unchecked((long)0x8000000000000000UL)) return 11;
                    if (BitConverter.DoubleToInt64Bits(Math.Max(-0.0, 0.0)) != 0) return 12;
                    if (Math.Clamp(12, 0, 10) != 10 || Math.Clamp(-1.0, 0.0, 2.0) != 0.0) return 13;
                    if (float.NaN.CompareTo(1.0f) >= 0 || 1.0f.CompareTo(float.NaN) <= 0) return 14;
                    if (((IComparable)double.NaN).CompareTo(double.NaN) != 0) return 17;
                    if (((IComparable)float.MaxValue).CompareTo(float.MinValue) <= 0) return 18;
                    if (float.Epsilon <= 0 || double.Epsilon <= 0) return 19;
                    if (!float.IsNegative(float.NegativeZero) ||
                        !double.IsNegative(double.NegativeZero)) return 20;
                    if (double.Parse("1.5") != 1.5 || float.Parse("-2.25") != -2.25f) return 21;
                    if (BitConverter.DoubleToInt64Bits(double.Parse("4.9406564584124654E-324")) != 1) return 22;
                    if (double.Parse("1.7976931348623157E+308") != double.MaxValue) return 23;
                    if (!double.IsInfinity(double.Parse("1E+309"))) return 24;
                    if (BitConverter.DoubleToInt64Bits(double.Parse("-1E-999")) !=
                        unchecked((long)0x8000000000000000UL)) return 25;
                    if (double.TryParse("1.2.3", out _)) return 26;
                    if (1.5.ToString() != "1.5" || double.Epsilon.ToString() != "5E-324") return 27;
                    if (double.MaxValue.ToString() != "1.7976931348623157E+308") return 28;
                    if (1_000_000_000_000_000.0.ToString() != "1000000000000000") return 29;
                    if (float.Epsilon.ToString() != "1E-45" ||
                        float.MaxValue.ToString() != "3.4028235E+38") return 30;
                    if (double.NaN.ToString() != "NaN" ||
                        double.NegativeInfinity.ToString() != "-Infinity") return 31;
                    var roundTripBits = new[]
                    {
                        1L,
                        unchecked((long)0x0010000000000000UL),
                        unchecked((long)0x3fd5555555555555UL),
                        unchecked((long)0x3ff0000000000001UL),
                        unchecked((long)0x7fefffffffffffffUL),
                        unchecked((long)0x8000000000000001UL),
                    };
                    for (var index = 0; index < roundTripBits.Length; index++)
                    {
                        var original = BitConverter.Int64BitsToDouble(roundTripBits[index]);
                        var parsed = double.Parse(original.ToString("R"));
                        if (BitConverter.DoubleToInt64Bits(parsed) != roundTripBits[index]) return 32;
                    }
                    try { _ = Math.Sign(double.NaN); return 15; }
                    catch (ArithmeticException) { }
                    try { _ = Math.Abs(int.MinValue); return 16; }
                    catch (OverflowException) { }
                    return input - input;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FloatingMathFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 4)]
    [InlineData(WasmTarget.Wasm64, 8)]
    public void CompilerExecutesNativeIntegerContracts(
        WasmTarget target,
        int expectedSize)
    {
        using var assets = TestAssets.Create();
        var source =
            """
            using System;

            namespace NativeIntegerEnumFixture;

            public static class EntryPoint
            {
                public static int Run(int expectedSize)
                {
                    if (IntPtr.Size != expectedSize || UIntPtr.Size != expectedSize) return 1;
                    if (nint.MinValue >= 0 || nint.MaxValue <= 0 || nuint.MinValue != 0) return 2;
                    if (nuint.MaxValue <= 0 || IntPtr.Zero != 0 || UIntPtr.Zero != 0) return 3;
                    var signed = (nint)(-42);
                    var unsigned = (nuint)42;
                    if (signed.CompareTo((nint)(-41)) >= 0 || unsigned.CompareTo((nuint)41) <= 0) return 4;
                    if (signed.ToString() != "-42" || unsigned.ToString("X") != "2A") return 5;
                    if (nint.Parse("-42") != signed || nuint.Parse("42") != unsigned) return 6;
                    if (!nint.TryParse("17", out var parsedSigned) || parsedSigned != 17) return 7;
                    if (!nuint.TryParse("17", out var parsedUnsigned) || parsedUnsigned != 17) return 8;
                    return 0;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "NativeIntegerEnumFixture",
            source,
            "NativeIntegerEnumFixture.EntryPoint",
            false,
            target,
            expectedSize,
            []));

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompilerExecutesEnumComparisonContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "EnumComparisonFixture",
            """
            using System;

            namespace EnumComparisonFixture;

            public enum SignedState : long { Low = -2, Same = 5, High = 9 }
            public enum UnsignedState : ulong { Low = 2, High = 0xffffffffffffffffUL }
            public enum OtherState : long { Same = 5 }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (((IComparable)SignedState.Low).CompareTo(SignedState.High) >= 0) return 1;
                    if (((IComparable)SignedState.Same).CompareTo(SignedState.Same) != 0) return 2;
                    if (((IComparable)UnsignedState.High).CompareTo(UnsignedState.Low) <= 0) return 3;
                    if (((IComparable)SignedState.High).CompareTo(null) != 1) return 4;
                    try
                    {
                        _ = ((IComparable)SignedState.Same).CompareTo(OtherState.Same);
                        return 5;
                    }
                    catch (ArgumentException)
                    {
                    }
                    return input - input;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EnumComparisonFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }
}
