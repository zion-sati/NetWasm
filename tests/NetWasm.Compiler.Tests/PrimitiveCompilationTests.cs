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
    public void CompilerExecutesByteAndSByteFidelityContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ByteSByteFidelityFixture",
            """
            using System;
            using System.Globalization;

            namespace ByteSByteFidelityFixture;

            public static class EntryPoint
            {
                public static int Run(int category)
                {
                    if (category == 1)
                    {
                        if (byte.Parse(" 255 ", NumberStyles.Integer, null) != byte.MaxValue) return 1;
                        if (sbyte.Parse(" -128 ", NumberStyles.Integer, null) != sbyte.MinValue) return 2;
                        if (byte.Parse("FF", NumberStyles.HexNumber, null) != byte.MaxValue) return 3;
                        if (sbyte.Parse("80", NumberStyles.HexNumber, null) != sbyte.MinValue) return 4;
                        if (byte.Parse("11111111", NumberStyles.BinaryNumber, null) != byte.MaxValue) return 5;
                        if (sbyte.Parse("10000000", NumberStyles.BinaryNumber, null) != sbyte.MinValue) return 6;
                        if (byte.Parse(new byte[] { (byte)'2', (byte)'5', (byte)'5' },
                            NumberStyles.Integer, null) != byte.MaxValue) return 7;
                        if (sbyte.Parse(new byte[] { (byte)'-', (byte)'1', (byte)'2', (byte)'8' },
                            NumberStyles.Integer, null) != sbyte.MinValue) return 8;
                        try { _ = byte.Parse("256", NumberStyles.Integer, null); return 9; }
                        catch (OverflowException) { }
                        try { _ = sbyte.Parse("invalid", NumberStyles.Integer, null); return 10; }
                        catch (FormatException) { }
                        return 0;
                    }

                    if (category == 2)
                    {
                        if (!byte.TryParsePartial("123tail", NumberStyles.Integer, null,
                            out var partialByte, out var byteChars) || partialByte != 123 || byteChars != 3) return 1;
                        var partialSByteUtf8 = new byte[] { (byte)'-', (byte)'1', (byte)'2', (byte)'x' };
                        if (!sbyte.TryParsePartial(partialSByteUtf8, NumberStyles.Integer, null,
                            out var partialSByte, out var sbyteBytes) || partialSByte != -12 || sbyteBytes != 3) return 2;
                        return 0;
                    }

                    if (category == 3)
                    {
                        if (byte.LeadingZeroCount(1) != 7 || byte.TrailingZeroCount(0x80) != 7 ||
                            byte.PopCount(0xF0) != 4 || byte.Log2(0x80) != 7) return 1;
                        if (byte.RotateLeft(0x81, 1) != 0x03 || byte.RotateRight(0x81, 1) != 0xC0) return 2;
                        return 0;
                    }

                    if (category == 4)
                    {
                        if (byte.CreateChecked(255) != byte.MaxValue ||
                            byte.CreateSaturating(-1) != byte.MinValue ||
                            byte.CreateSaturating(300) != byte.MaxValue ||
                            byte.CreateTruncating(-1) != byte.MaxValue ||
                            byte.CreateTruncating(256) != byte.MinValue) return 1;
                        if (sbyte.CreateChecked(127) != sbyte.MaxValue ||
                            sbyte.CreateSaturating(-200) != sbyte.MinValue ||
                            sbyte.CreateSaturating(200) != sbyte.MaxValue ||
                            sbyte.CreateTruncating(128) != sbyte.MinValue ||
                            sbyte.CreateTruncating(255) != -1) return 2;
                        return 0;
                    }

                    if (category == 5)
                    {
                        if (byte.CreateSaturating(300.0) != byte.MaxValue) return 1;
                        return 0;
                    }

                    if (category == 6)
                    {
                        if (sbyte.CreateSaturating(-200.0m) != sbyte.MinValue) return 1;
                        return 0;
                    }

                    if (category == 7)
                    {
                        try { _ = byte.CreateChecked(256); return 1; }
                        catch (OverflowException) { }
                        try { _ = sbyte.CreateChecked(128); return 2; }
                        catch (OverflowException) { }
                        return 0;
                    }

                    return 99;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ByteSByteFidelityFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 1));
        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 2));
        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 3));
        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 4));
        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 5));
        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 6));
        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 7));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesInt128AndUInt128ParsingContracts(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Globalization;

            namespace Int128ParsingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (Int128.Parse("-170141183460469231731687303715884105728") != Int128.MinValue) return 1;
                    if (Int128.Parse("170141183460469231731687303715884105727") != Int128.MaxValue) return 2;
                    if (UInt128.Parse("340282366920938463463374607431768211455") != UInt128.MaxValue) return 3;
                    if (Int128.Parse("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber) != -1) return 4;
                    if (UInt128.Parse("11111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111", NumberStyles.BinaryNumber) != UInt128.MaxValue) return 5;

                    var numberStyle = NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint;
                    if (Int128.Parse("1,234.000", numberStyle, null) != 1234) return 6;
                    if (Int128.Parse("12e2", NumberStyles.AllowExponent, null) != 1200) return 7;
                    if (Int128.Parse("120e-1", NumberStyles.AllowExponent, null) != 12) return 8;
                    if (Int128.TryParse("12.5", NumberStyles.AllowDecimalPoint, null, out _)) return 9;
                    if (Int128.Parse("(42)", NumberStyles.AllowParentheses, null) != -42) return 10;
                    if (UInt128.Parse("$42", NumberStyles.AllowCurrencySymbol, null) != 42) return 11;
                    if (Int128.Parse("42-", NumberStyles.AllowTrailingSign, null) != -42) return 12;

                    var utf8 = new byte[] { (byte)'3', (byte)'4', (byte)'0', (byte)'2', (byte)'8', (byte)'2',
                        (byte)'3', (byte)'6', (byte)'6', (byte)'9', (byte)'2', (byte)'0', (byte)'9', (byte)'3',
                        (byte)'8', (byte)'4', (byte)'6', (byte)'3', (byte)'4', (byte)'6', (byte)'3', (byte)'3',
                        (byte)'7', (byte)'4', (byte)'6', (byte)'0', (byte)'7', (byte)'4', (byte)'3', (byte)'1',
                        (byte)'7', (byte)'6', (byte)'8', (byte)'2', (byte)'1', (byte)'1', (byte)'4', (byte)'5',
                        (byte)'5' };
                    if (UInt128.Parse(utf8, NumberStyles.Integer, null) != UInt128.MaxValue) return 13;
                    if (!Int128.TryParsePartial("123tail", NumberStyles.Integer, null,
                        out var partial, out var consumed) || partial != 123 || consumed != 3) return 14;

                    try { _ = UInt128.Parse("340282366920938463463374607431768211456"); return 15; }
                    catch (OverflowException) { }
                    try { _ = Int128.Parse("170141183460469231731687303715884105728"); return 16; }
                    catch (OverflowException) { }
                    try { _ = Int128.Parse("0x2A", NumberStyles.HexNumber); return 17; }
                    catch (FormatException) { }

                    return input - input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "Int128ParsingFixture",
            source,
            "Int128ParsingFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
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
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesTargetTypedFloatingParseContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Globalization;
            using System.Numerics;

            namespace FloatingParseFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    const string singleAboveMidpoint =
                        "1.000000059604644830901776231257827021181583404541015625";
                    if (BitConverter.SingleToUInt32Bits(float.Parse(
                        singleAboveMidpoint, NumberStyles.Float, null)) != 0x3F80_0001U) return 1;

                    const string halfAboveMidpoint =
                        "1.0004882812500000000000000000000000001";
                    if (BitConverter.HalfToUInt16Bits(Half.Parse(
                        halfAboveMidpoint, NumberStyles.Float, null)) != 0x3C01) return 2;

                    const string bfloatAboveMidpoint =
                        "1.0039062500000000000000000000000000001";
                    if (BitConverter.BFloat16ToUInt16Bits(BFloat16.Parse(
                        bfloatAboveMidpoint, NumberStyles.Float, null)) != 0x3F81) return 3;

                    var groupedStyle = NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint;
                    if (!float.TryParse("1,234.5", groupedStyle, null, out var grouped) ||
                        grouped != 1234.5f) return 4;
                    if (float.TryParse("1.5", NumberStyles.None, null, out _)) return 5;
                    if (!float.TryParse("1.5", NumberStyles.AllowDecimalPoint, null, out var fraction) ||
                        fraction != 1.5f) return 6;
                    if (float.TryParse("+1", NumberStyles.None, null, out _)) return 7;
                    if (!float.TryParse("+1", NumberStyles.AllowLeadingSign, null, out var signed) ||
                        signed != 1.0f) return 8;
                    if (double.TryParse(" 1 ", NumberStyles.None, null, out _)) return 9;
                    if (!double.TryParse(" 1 ", NumberStyles.AllowLeadingWhite |
                        NumberStyles.AllowTrailingWhite, null, out var spaced) || spaced != 1.0) return 10;
                    if (double.TryParse("1e2", NumberStyles.None, null, out _)) return 11;
                    if (!double.TryParse("1e2", NumberStyles.AllowExponent, null, out var exponent) ||
                        exponent != 100.0) return 12;
                    if (!double.TryParse("42-", NumberStyles.AllowTrailingSign, null, out var trailingSign) ||
                        trailingSign != -42.0) return 13;
                    if (!double.TryParse("(42.5)", NumberStyles.AllowParentheses |
                        NumberStyles.AllowDecimalPoint, null, out var parenthesized) ||
                        parenthesized != -42.5) return 14;
                    if (!double.TryParse("$1.5", NumberStyles.AllowCurrencySymbol |
                        NumberStyles.AllowDecimalPoint, null, out var currency) || currency != 1.5) return 15;

                    if (!float.TryParsePartial("12.5tail", NumberStyles.Float, null,
                        out var partial, out var charsConsumed) || partial != 12.5f || charsConsumed != 4) return 16;
                    if (float.TryParse("12.5tail", NumberStyles.Float, null, out _)) return 17;
                    var utf8 = new byte[] { (byte)'1', (byte)'2', (byte)'.', (byte)'5', (byte)'x' };
                    if (!float.TryParsePartial(utf8, NumberStyles.Float, null,
                        out partial, out var bytesConsumed) || partial != 12.5f || bytesConsumed != 4) return 18;
                    if (!double.TryParsePartial("NaNtail", NumberStyles.None, null,
                        out var special, out charsConsumed) || !double.IsNaN(special) || charsConsumed != 3) return 19;

                    var longUtf8 = new byte[800];
                    for (var index = 0; index < longUtf8.Length; index++) longUtf8[index] = (byte)'9';
                    if (!double.TryParse(longUtf8, NumberStyles.None, null, out var huge) ||
                        !double.IsPositiveInfinity(huge)) return 21;

                    if (BitConverter.BFloat16ToUInt16Bits((BFloat16)257U) != 0x4380) return 22;
                    if (BitConverter.BFloat16ToUInt16Bits((BFloat16)259U) != 0x4382) return 23;
                    if (BitConverter.BFloat16ToUInt16Bits((BFloat16)uint.MaxValue) != 0x4F80) return 24;
                    if (BitConverter.BFloat16ToUInt16Bits((BFloat16)ulong.MaxValue) != 0x5F80) return 25;
                    if (BitConverter.BFloat16ToUInt16Bits((BFloat16)int.MinValue) != 0xCF00) return 26;

                    try
                    {
                        _ = float.TryParse("1", NumberStyles.HexNumber, null, out _);
                        return 20;
                    }
                    catch (ArgumentException)
                    {
                    }

                    return input - input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "FloatingParseFixture",
            source,
            "FloatingParseFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesWideFloatFormattingAndDecimalRemainderContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Numerics;

            namespace WideFloatAndDecimalRemainderFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var half = BitConverter.UInt16BitsToHalf(0x2E66);
                    if (half.ToString() != "0.1" || half.ToString("R", null) != "0.1") return 1;
                    if (BitConverter.HalfToUInt16Bits(Half.Parse(half.ToString())) != 0x2E66) return 2;
                    if (half.ToString("F3", null) != "0.100") return 3;

                    var bfloat = BitConverter.UInt16BitsToBFloat16(0x3DCD);
                    if (bfloat.ToString() != "0.1" || bfloat.ToString("R", null) != "0.1") return 4;
                    if (BitConverter.BFloat16ToUInt16Bits(BFloat16.Parse(bfloat.ToString())) != 0x3DCD) return 5;
                    if (bfloat.ToString("F3", null) != "0.100") return 6;

                    var smaller = 1.2m % 10.00m;
                    var bits = decimal.GetBits(smaller);
                    if (smaller.ToString() != "1.2" || bits[0] != 12 || bits[1] != 0 ||
                        bits[2] != 0 || bits[3] != 0x0001_0000) return 7;

                    var negativeSmaller = -1.2m % -10.00m;
                    bits = decimal.GetBits(negativeSmaller);
                    if (negativeSmaller.ToString() != "-1.2" || bits[0] != 12 ||
                        bits[3] != unchecked((int)0x8001_0000U)) return 8;

                    var negativeZero = new decimal(0, 0, 0, true, 3);
                    var preservedZero = negativeZero % 2.00m;
                    bits = decimal.GetBits(preservedZero);
                    if (bits[0] != 0 || bits[1] != 0 || bits[2] != 0 ||
                        bits[3] != unchecked((int)0x8003_0000U)) return 9;

                    var equal = 1.20m % 1.2m;
                    bits = decimal.GetBits(equal);
                    if (bits[0] != 0 || bits[1] != 0 || bits[2] != 0 ||
                        bits[3] != 0x0002_0000) return 10;

                    return input - input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "WideFloatAndDecimalRemainderFixture",
            source,
            "WideFloatAndDecimalRemainderFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesFutureRoundingAndInvariantCurrencyContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Globalization;

            namespace FutureRoundingAndCurrencyFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var doubleValue = BitConverter.UInt64BitsToDouble(0x3FBF_9ADD_3746_F65EUL);
                    if (BitConverter.DoubleToUInt64Bits(Math.Round(doubleValue, 16)) !=
                        0x3FBF_9ADD_3746_F661UL) return 1;
                    if (BitConverter.DoubleToUInt64Bits(Math.Round(doubleValue, 17)) !=
                        0x3FBF_9ADD_3746_F65EUL) return 2;
                    if (BitConverter.DoubleToUInt64Bits(Math.Round(doubleValue, int.MaxValue)) !=
                        0x3FBF_9ADD_3746_F65EUL) return 3;

                    var singleValue = BitConverter.UInt32BitsToSingle(0x3DFC_D6EAU);
                    if (BitConverter.SingleToUInt32Bits(MathF.Round(singleValue, 7)) !=
                        0x3DFC_D6EBU) return 4;
                    if (BitConverter.SingleToUInt32Bits(MathF.Round(singleValue, 8)) !=
                        0x3DFC_D6EAU) return 5;
                    if (BitConverter.SingleToUInt32Bits(MathF.Round(singleValue, int.MaxValue)) !=
                        0x3DFC_D6EAU) return 6;

                    try { _ = Math.Round(1.0, -1); return 7; }
                    catch (ArgumentOutOfRangeException) { }
                    try { _ = MathF.Round(1.0f, -1); return 8; }
                    catch (ArgumentOutOfRangeException) { }

                    var currencyStyle = NumberStyles.AllowCurrencySymbol |
                        NumberStyles.AllowDecimalPoint;
                    if (!decimal.TryParse("$1.2", currencyStyle, null, out var currency) ||
                        currency != 1.2m) return 9;
                    if (decimal.TryParse("1$2", currencyStyle, null, out _)) return 10;
                    if (decimal.TryParse("$$1.2", currencyStyle, null, out _)) return 11;
                    if (decimal.TryParse("1.2$$", currencyStyle, null, out _)) return 12;

                    return input - input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "FutureRoundingAndCurrencyFixture",
            source,
            "FutureRoundingAndCurrencyFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesInvariantStandardFloatingFormats(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Numerics;

            namespace StandardFloatingFormatFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    const double doubleValue = 12345.6789;
                    if (doubleValue.ToString("E") != "1.234568E+004") return 1;
                    if (doubleValue.ToString("e2") != "1.23e+004") return 2;
                    if (doubleValue.ToString("E0") != "1E+004") return 3;
                    if (doubleValue.ToString("G3") != "1.23E+04") return 4;
                    if (doubleValue.ToString("g5") != "12346") return 5;
                    if (doubleValue.ToString("G0") != "12345.6789") return 6;

                    const float singleValue = 0.0012345678f;
                    if (singleValue.ToString("E") != "1.234568E-003") return 7;
                    if (singleValue.ToString("e2") != "1.23e-003") return 8;
                    if (singleValue.ToString("G3") != "0.00123") return 9;
                    if (singleValue.ToString("g5") != "0.0012346") return 10;

                    var half = BitConverter.UInt16BitsToHalf(0x2E66);
                    if (half.ToString("E", null) != "9.997559E-002") return 11;
                    if (half.ToString("e2", null) != "1.00e-001") return 12;
                    if (half.ToString("G3", null) != "0.1") return 13;
                    if (half.ToString("g5", null) != "0.099976") return 14;

                    var bfloat = BitConverter.UInt16BitsToBFloat16(0x3DCD);
                    if (bfloat.ToString("E", null) != "1.000977E-001") return 15;
                    if (bfloat.ToString("e2", null) != "1.00e-001") return 16;
                    if (bfloat.ToString("G3", null) != "0.1") return 17;
                    if (bfloat.ToString("g5", null) != "0.1001") return 18;

                    if (double.PositiveInfinity.ToString("E2") != "Infinity") return 19;
                    // As .NET 11 formats them, R ignores a precision.
                    if (doubleValue.ToString("R2") != "12345.6789" || 0.1.ToString("R0") != "0.1" ||
                        1e21.ToString("r3") != "1e+21" || 1.5f.ToString("R7") != "1.5") return 20;

                    Span<char> chars = stackalloc char[16];
                    if (!doubleValue.TryFormat(chars, out var written, "E2") ||
                        chars[..written].ToString() != "1.23E+004") return 21;
                    if (doubleValue.TryFormat(chars[..4], out written, "E2") || written != 0) return 22;

                    Span<byte> utf8 = stackalloc byte[16];
                    if (!0.126.TryFormat(utf8, out written, "0.0‰", null)) return 23;
                    if (written != 8) return 24;
                    if (utf8[5] != 0xE2) return 25;
                    if (utf8[6] != 0x80) return 26;
                    if (utf8[7] != 0xB0) return 27;

                    return input - input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "StandardFloatingFormatFixture",
            source,
            "StandardFloatingFormatFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesInvariantCustomAndIntegerFormats(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Numerics;

            namespace InvariantCustomFormatFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if ((-1234).ToString("00000") != "-01234") return 1;
                    if (1234U.ToString("#,##0.00") != "1,234.00") return 2;
                    if ((-1234567890123L).ToString("0.00E+00") != "-1.23E+12") return 3;
                    if (((Int128)(-1234567890123L)).ToString("#,##0.00", null) !=
                        "-1,234,567,890,123.00") return 4;

                    var big = BigInteger.Parse("-123456789012345678901234567890");
                    if (big.ToString("0.00E+00", null) != "-1.23E+29") return 5;
                    if (big.ToString("N1", null) !=
                        "-123,456,789,012,345,678,901,234,567,890.0") return 6;

                    if ((-1234.5678m).ToString("#,##0.00") != "-1,234.57") return 7;
                    if ((-1234.5678).ToString("0.00;[0.00];zero") != "[1234.57]") return 8;
                    if ((-1234.5678f).ToString("'v='0") != "-v=1235") return 9;
                    if (0m.ToString("0.00;[0.00];zero") != "zero") return 10;

                    if ((-1234).ToString("F3") != "-1234.000") return 11;
                    if ((-1234).ToString("N1") != "-1,234.0") return 12;
                    if ((-1234).ToString("E2") != "-1.23E+003") return 13;
                    if ((-1234).ToString("P1") != "-123,400.0 %") return 14;
                    if ((-1234).ToString("C") != "(\u00a41,234.00)") return 15;

                    var half = BitConverter.UInt16BitsToHalf(0x2E66);
                    if (half.ToString("0.0000", null) != "0.1000") return 21;
                    var bfloat = BitConverter.UInt16BitsToBFloat16(0x3DCD);
                    if (bfloat.ToString("0.0000", null) != "0.1001") return 22;
                    if (123456789.ToString("0,,") != "123") return 23;
                    if (0.126.ToString("0.0%") != "12.6%") return 24;
                    if (0.126.ToString("0.0‰") != "126.0‰") return 25;
                    if (9.96.ToString("0.0") != "10.0") return 26;
                    if ((-0.0).ToString("0.00") != "-0.00") return 27;
                    if (12.ToString(@"000\#") != "012#") return 28;
                    if (0.ToString("positive;negative;zero") != "zero") return 29;
                    if (0.ToString("#") != "") return 30;

                    Span<char> chars = stackalloc char[32];
                    if (!(-1234).TryFormat(chars, out var written, "#,##0.00") ||
                        chars[..written].ToString() != "-1,234.00") return 19;

                    Span<byte> utf8 = stackalloc byte[32];
                    if (!big.TryFormat(utf8, out written, "0.00E+00", null) ||
                        written != 9 || utf8[0] != (byte)'-' || utf8[8] != (byte)'9') return 20;

                    if (!(-1234.5678m).TryFormat(chars, out written, "#,##0.00", null) ||
                        chars[..written].ToString() != "-1,234.57") return 31;
                    if ((-1234.5678m).TryFormat(chars[..4], out written, "#,##0.00", null) ||
                        written != 0) return 32;
                    if (!0.126m.TryFormat(utf8, out written, "0.0‰", null) || written != 8 ||
                        utf8[5] != 0xE2 || utf8[6] != 0x80 || utf8[7] != 0xB0) return 33;

                    if (!((Int128)(-1234)).TryFormat(chars, out written, "#,##0.00", null) ||
                        chars[..written].ToString() != "-1,234.00") return 34;
                    nint nativeSigned = -42;
                    if (!nativeSigned.TryFormat(chars, out written, "D4", null) ||
                        chars[..written].ToString() != "-0042") return 35;
                    nuint nativeUnsigned = 42;
                    if (!nativeUnsigned.TryFormat(chars, out written, "X4", null) ||
                        chars[..written].ToString() != "002A") return 36;
                    if (!new BigInteger(12).TryFormat(utf8, out written, "0‰", null) || written != 8 ||
                        utf8[5] != 0xE2 || utf8[6] != 0x80 || utf8[7] != 0xB0) return 37;

                    return input - input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "InvariantCustomFormatFixture",
            source,
            "InvariantCustomFormatFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesLinearDecimalPartialParsing(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Globalization;
            using System.Numerics;

            namespace DecimalPartialParseFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (!decimal.TryParsePartial("123.45tail", NumberStyles.Any, null,
                        out var value, out var consumed) || value != 123.45m || consumed != 6) return 1;
                    if (!decimal.TryParsePartial("1e+2x", NumberStyles.Float, null,
                        out value, out consumed) || value != 100m || consumed != 4) return 2;
                    if (!decimal.TryParsePartial("1e+x", NumberStyles.Float, null,
                        out value, out consumed) || value != 1m || consumed != 1) return 3;

                    const string overflow = "999999999999999999999999999999x";
                    if (decimal.TryParsePartial(overflow, NumberStyles.Any, null,
                        out value, out consumed) || value != 0m || consumed != 0) return 4;

                    if (!decimal.TryParsePartial("($1.25)tail".AsSpan(), NumberStyles.Any, null,
                        out value, out consumed) || value != -1.25m || consumed != 7) return 5;
                    if (!decimal.TryParsePartial("$-12.5tail".AsSpan(), NumberStyles.Any, null,
                        out value, out consumed) || value != -12.5m || consumed != 6) return 6;
                    if (!decimal.TryParsePartial("123$-tail", NumberStyles.Any, null,
                        out value, out consumed) || value != -123m || consumed != 5) return 7;

                    ReadOnlySpan<byte> utf8 = "42.5rest"u8;
                    if (!decimal.TryParsePartial(utf8, NumberStyles.Any, null,
                        out value, out consumed) || value != 42.5m || consumed != 4) return 8;

                    var longUtf8 = new byte[201];
                    for (var index = 0; index < 200; index++) longUtf8[index] = (byte)'0';
                    longUtf8[200] = (byte)'x';
                    if (!decimal.TryParsePartial(longUtf8, NumberStyles.Any, null,
                        out value, out consumed) || value != 0m || consumed != 200) return 9;

                    if (!Decimal32.TryParse("1.25".AsSpan(), NumberStyles.Float, null, out var d32) ||
                        d32.ToString("G", null) != "1.25") return 10;
                    if (!Decimal64.TryParse("1.25"u8, null, out var d64) ||
                        d64.ToString("G", null) != "1.25") return 11;
                    if (!Decimal128.TryParsePartial("1.25tail".AsSpan(), NumberStyles.Float, null,
                        out var d128, out consumed) || d128.ToString("G", null) != "1.25" ||
                        consumed != 4) return 12;
                    if (!Decimal128.TryParsePartial("NaNtail"u8, NumberStyles.None, null,
                        out d128, out consumed) || !Decimal128.IsNaN(d128) || consumed != 3) return 13;

                    Span<char> chars = stackalloc char[32];
                    if (!d32.TryFormat(chars, out var written, "E2", null) ||
                        chars[..written].ToString() != "1.25E+000") return 14;

                    Span<byte> utf8Destination = stackalloc byte[32];
                    if (!d64.TryFormat(utf8Destination, out written, "F2", null) || written != 4 ||
                        utf8Destination[0] != (byte)'1' || utf8Destination[1] != (byte)'.' ||
                        utf8Destination[2] != (byte)'2' || utf8Destination[3] != (byte)'5') return 15;

                    if (d32.TryFormat(chars[..3], out written, "E2", null) || written != 0) return 16;
                    var grouped = Decimal64.Parse("1234.5", null);
                    if (!grouped.TryFormat(chars, out written, "N2", null) ||
                        chars[..written].ToString() != "1,234.50") return 17;

                    return input - input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "DecimalPartialParseFixture",
            source,
            "DecimalPartialParseFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesBigIntegerByteWritingContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Numerics;

            namespace BigIntegerByteWritingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Span<byte> destination = stackalloc byte[32];
                    // IBinaryInteger writes whole limbs, as .NET 11 does: nint.Size bytes each.
                    if (BigInteger.Zero.GetByteCount() != 1 ||
                        !WriteLittleEndian(BigInteger.Zero, destination, out var written) ||
                        written != ByteCount(BigInteger.Zero) || written % IntPtr.Size != 0 ||
                        destination[..written].IndexOfAnyExcept((byte)0) >= 0) return 1;

                    var positive = new BigInteger(33022);
                    if (positive.GetByteCount() != 3 || positive.GetByteCount(isUnsigned: true) != 2) return 2;
                    if (!positive.TryWriteBytes(destination, out written) || written != 3 ||
                        destination[0] != 0xFE || destination[1] != 0x80 || destination[2] != 0) return 3;
                    if (!positive.TryWriteBytes(destination, out written, isUnsigned: true, isBigEndian: true) ||
                        written != 2 || destination[0] != 0x80 || destination[1] != 0xFE) return 4;

                    Span<byte> small = stackalloc byte[2];
                    small[0] = 0xA5;
                    small[1] = 0x5A;
                    if (positive.TryWriteBytes(small, out written) || written != 0 ||
                        small[0] != 0xA5 || small[1] != 0x5A) return 5;

                    var negative = new BigInteger(-129);
                    if (negative.GetByteCount() != 2 ||
                        !WriteBigEndian(negative, destination, out written) || written != ByteCount(negative) ||
                        destination[written - 1] != 0x7F || destination[..(written - 1)].IndexOfAnyExcept((byte)0xFF) >= 0) return 6;
                    var negativeBoundary = new BigInteger(-128);
                    if (negativeBoundary.GetByteCount() != 1 ||
                        !WriteLittleEndian(negativeBoundary, destination, out written) || written != ByteCount(negativeBoundary) ||
                        destination[0] != 0x80 || destination[1..written].IndexOfAnyExcept((byte)0xFF) >= 0) return 11;

                    try
                    {
                        negative.TryWriteBytes(destination, out _, isUnsigned: true);
                        return 7;
                    }
                    catch (OverflowException)
                    {
                    }

                    var allocated = positive.ToByteArray(isUnsigned: false, isBigEndian: true);
                    if (allocated.Length != 3 || allocated[0] != 0 || allocated[1] != 0x80 || allocated[2] != 0xFE) return 8;

                    var large = (BigInteger.One << 72) + 0x80FE;
                    if (!WriteBigEndian(large, destination, out written) ||
                        new BigInteger(destination[..written], isUnsigned: false, isBigEndian: true) != large) return 9;

                    var largeNegative = -((BigInteger.One << 72) + 1);
                    if (!WriteLittleEndian(largeNegative, destination, out written) ||
                        new BigInteger(destination[..written], isUnsigned: false, isBigEndian: false) != largeNegative) return 10;

                    var carryAcrossLimbs = -(BigInteger.One << 64);
                    if (!WriteLittleEndian(carryAcrossLimbs, destination, out written) || written != ByteCount(carryAcrossLimbs) ||
                        destination[0] != 0 || destination[7] != 0 || destination[8..written].IndexOfAnyExcept((byte)0xFF) >= 0) return 12;

                    return input - input;
                }

                // .NET implements these IBinaryInteger members explicitly.
                private static int ByteCount<T>(T value) where T : IBinaryInteger<T> => value.GetByteCount();

                private static bool WriteLittleEndian<T>(T value, Span<byte> destination, out int written) where T : IBinaryInteger<T> =>
                    value.TryWriteLittleEndian(destination, out written);

                private static bool WriteBigEndian<T>(T value, Span<byte> destination, out int written) where T : IBinaryInteger<T> =>
                    value.TryWriteBigEndian(destination, out written);
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "BigIntegerByteWritingFixture",
            source,
            "BigIntegerByteWritingFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesBigIntegerModPowContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;
            using System.Numerics;

            namespace BigIntegerModPowFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (BigInteger.ModPow(4, 13, 497) != 445) return 1;
                    if (BigInteger.ModPow(-4, 13, 497) != -445) return 2;
                    if (BigInteger.ModPow(-4, 12, -497) != 484) return 3;
                    if (BigInteger.ModPow(0, 0, 17) != 1 || BigInteger.ModPow(7, 0, 1) != 0) return 4;

                    var value = BigInteger.Parse("123456789012345678901234567890");
                    var exponent = BigInteger.Parse("12345678901234567890");
                    var oddModulus = BigInteger.Parse("98765432109876543210987654321");
                    var evenModulus = BigInteger.Parse("98765432109876543210987654320");
                    if (BigInteger.ModPow(value, exponent, oddModulus) !=
                        BigInteger.Parse("1524114900152411490015241149")) return 5;
                    if (BigInteger.ModPow(value, exponent, evenModulus) !=
                        BigInteger.Parse("55058177645209918478091210960")) return 6;

                    var wideValue = (BigInteger.One << 1024) + 123;
                    var wideModulus = (BigInteger.One << 192) + 0x12345;
                    if (BigInteger.ModPow(wideValue, 17, wideModulus) !=
                        BigInteger.Parse("6203164243274996771075756979330786038217354427568729058089")) return 7;
                    var wideExponent = (BigInteger.One << 130) + 77;
                    var wideBase = (BigInteger.One << 256) + 12345;
                    if (BigInteger.ModPow(wideBase, wideExponent, wideModulus) !=
                        BigInteger.Parse("1963275571077542758993233123309182553439614207184651901951")) return 10;

                    try
                    {
                        _ = BigInteger.ModPow(2, -1, 0);
                        return 8;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }

                    try
                    {
                        _ = BigInteger.ModPow(2, 1, 0);
                        return 9;
                    }
                    catch (DivideByZeroException)
                    {
                    }

                    return input - input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "BigIntegerModPowFixture",
            source,
            "BigIntegerModPowFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
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
