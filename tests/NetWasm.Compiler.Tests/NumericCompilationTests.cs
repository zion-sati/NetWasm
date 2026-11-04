using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class NumericCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void HyperbolicCosinePreservesTheFiniteOverflowBoundary(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"CoshFiniteBoundary_{optimize}_{target}",
            """
            using System;

            namespace CoshFiniteBoundary;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var at710 = Math.Cosh(710.0);
                    var at710Point4 = Math.Cosh(710.4);
                    var nearLimit = Math.Cosh(710.4758600739439);
                    if (!double.IsFinite(at710) || !double.IsFinite(at710Point4) ||
                        !double.IsFinite(nearLimit) ||
                        Math.Cosh(710.5) != double.PositiveInfinity ||
                        Math.Cosh(-710.0) != at710)
                    {
                        return 1;
                    }

                    if (!WithinUlps(at710, 0x7FE3E21A464507F9UL, 4) ||
                        !WithinUlps(at710Point4, 0x7FEDA98A7371610BUL, 4) ||
                        !WithinUlps(nearLimit, 0x7FEFFFFFFFFFFD3BUL, 4))
                    {
                        return 2;
                    }

                    return 42;
                }

                private static bool WithinUlps(double value, ulong expected, ulong budget)
                {
                    var actual = BitConverter.DoubleToUInt64Bits(value);
                    var distance = actual >= expected
                        ? actual - expected
                        : expected - actual;
                    return distance <= budget;
                }
            }
            """,
            "CoshFiniteBoundary.EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void BaseConversionsPreserveSignsBoundsAndExceptionKinds(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"BaseConversionContracts_{optimize}_{target}",
            """
            using System;

            namespace BaseConversionContracts;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (Convert.ToByte("+FF", 16) != byte.MaxValue ||
                        Convert.ToInt32("+0xFF", 16) != 255 ||
                        Convert.ToSByte("FF", 16) != -1 ||
                        Convert.ToInt16("FFFF", 16) != -1 ||
                        Convert.ToUInt16("+FFFF", 16) != ushort.MaxValue ||
                        Convert.ToInt32("FFFFFFFF", 16) != -1 ||
                        Convert.ToUInt32("+FFFFFFFF", 16) != uint.MaxValue ||
                        Convert.ToInt64("FFFFFFFFFFFFFFFF", 16) != -1 ||
                        Convert.ToUInt64("+FFFFFFFFFFFFFFFF", 16) != ulong.MaxValue ||
                        Convert.ToInt32("-2147483648", 10) != int.MinValue ||
                        Convert.ToInt64("-9223372036854775808", 10) != long.MinValue)
                    {
                        return 1;
                    }

                    var failures = 0;
                    try { _ = Convert.ToInt32(null, 3); }
                    catch (ArgumentException) { failures++; }
                    try { _ = Convert.ToInt32(string.Empty, 10); }
                    catch (ArgumentOutOfRangeException) { failures++; }
                    try { _ = Convert.ToInt32("12x", 10); }
                    catch (FormatException) { failures++; }
                    try { _ = Convert.ToUInt32("12x", 10); }
                    catch (FormatException) { failures++; }
                    try { _ = Convert.ToInt32("-1", 16); }
                    catch (ArgumentException) { failures++; }
                    try { _ = Convert.ToUInt32("-1", 10); }
                    catch (OverflowException) { failures++; }
                    try { _ = Convert.ToInt32("+", 10); }
                    catch (FormatException) { failures++; }
                    try { _ = Convert.ToInt32("0x", 16); }
                    catch (FormatException) { failures++; }
                    try { _ = Convert.ToByte("256", 10); }
                    catch (OverflowException) { failures++; }
                    try { _ = Convert.ToInt32("2147483648", 10); }
                    catch (OverflowException) { failures++; }
                    try { _ = Convert.ToInt64("9223372036854775808", 10); }
                    catch (OverflowException) { failures++; }
                    try { _ = Convert.ToUInt64("18446744073709551616", 10); }
                    catch (OverflowException) { failures++; }

                    return failures == 12 ? 42 : -failures;
                }
            }
            """,
            "BaseConversionContracts.EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DefaultParsersPreservePinnedNet11WhitespaceAndGroupingContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"DefaultParserContracts_{optimize}_{target}",
            """
            using System;

            namespace DefaultParserContracts;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    const string integer = "\t42\r";
                    if (Convert.ToInt16(integer) != 42 ||
                        Convert.ToInt16(integer, null) != 42 ||
                        Convert.ToInt16((object)integer) != 42 ||
                        Convert.ToInt16((object)integer, null) != 42 ||
                        Convert.ToUInt16(integer) != 42 ||
                        Convert.ToUInt16(integer, null) != 42 ||
                        Convert.ToUInt16((object)integer) != 42 ||
                        Convert.ToUInt16((object)integer, null) != 42 ||
                        Convert.ToInt32(integer) != 42 ||
                        Convert.ToInt32(integer, null) != 42 ||
                        Convert.ToInt32((object)integer) != 42 ||
                        Convert.ToInt32((object)integer, null) != 42 ||
                        Convert.ToUInt32(integer) != 42 ||
                        Convert.ToUInt32(integer, null) != 42 ||
                        Convert.ToUInt32((object)integer) != 42 ||
                        Convert.ToUInt32((object)integer, null) != 42 ||
                        Convert.ToInt64(integer) != 42 ||
                        Convert.ToInt64(integer, null) != 42 ||
                        Convert.ToInt64((object)integer) != 42 ||
                        Convert.ToInt64((object)integer, null) != 42 ||
                        Convert.ToUInt64(integer) != 42 ||
                        Convert.ToUInt64(integer, null) != 42 ||
                        Convert.ToUInt64((object)integer) != 42 ||
                        Convert.ToUInt64((object)integer, null) != 42)
                    {
                        return 1;
                    }

                    const string grouped = "1,,234,";
                    if (Convert.ToSingle(grouped) != 1234 ||
                        Convert.ToSingle(grouped, null) != 1234 ||
                        Convert.ToSingle((object)grouped) != 1234 ||
                        Convert.ToSingle((object)grouped, null) != 1234 ||
                        Convert.ToDouble(grouped) != 1234 ||
                        Convert.ToDouble(grouped, null) != 1234 ||
                        Convert.ToDouble((object)grouped) != 1234 ||
                        Convert.ToDouble((object)grouped, null) != 1234 ||
                        Convert.ToDecimal(grouped) != 1234m ||
                        Convert.ToDecimal(grouped, null) != 1234m ||
                        Convert.ToDecimal((object)grouped) != 1234m ||
                        Convert.ToDecimal((object)grouped, null) != 1234m)
                    {
                        return 2;
                    }

                    const string boolean = "\u2000True\0";
                    if (!Convert.ToBoolean(boolean) ||
                        !Convert.ToBoolean(boolean, null) ||
                        !Convert.ToBoolean((object)boolean) ||
                        !Convert.ToBoolean((object)boolean, null))
                    {
                        return 3;
                    }

                    if (int.TryParse("\u200042\u2000", out _) ||
                        Int128.TryParse("\u200042\u2000", out _) ||
                        UInt128.TryParse("\u200042\u2000", out _) ||
                        float.TryParse("\u200042\u2000", out _) ||
                        decimal.TryParse("\u200042\u2000", out _) ||
                        float.TryParse(",123", out _) ||
                        float.TryParse("1.2,3", out _) ||
                        decimal.TryParse(",123", out _) ||
                        decimal.TryParse("1.2,3", out _) ||
                        decimal.TryParse("1e2", out _))
                    {
                        return 4;
                    }

                    if (!Int128.TryParse("\t42\r", out var signedWide) || signedWide != 42 ||
                        !UInt128.TryParse("\t42\r", out var unsignedWide) || unsignedWide != 42)
                    {
                        return 6;
                    }

                    if (Convert.ToUInt16("-0") != 0 ||
                        Convert.ToUInt32("-00") != 0 ||
                        Convert.ToUInt64("-000") != 0 ||
                        !int.TryParse("42 \0\0", out var nulInt) || nulInt != 42 ||
                        !float.TryParse("42 \0\0", out var nulFloat) || nulFloat != 42 ||
                        !decimal.TryParse("42 \0\0", out var nulDecimal) || nulDecimal != 42m ||
                        int.TryParse("42\0 ", out _) ||
                        float.TryParse("42\0 ", out _) ||
                        decimal.TryParse("42\0 ", out _))
                    {
                        return 9;
                    }

                    var canonicalNaN = BitConverter.SingleToUInt32Bits(float.NaN);
                    var utf8NaN = new byte[]
                    {
                        0xE2, 0x80, 0x80, (byte)'N', (byte)'a', (byte)'N', 0xE2, 0x80, 0x80,
                    };
                    if (!float.TryParse("\u2000NaN\u2000", out var unicodeNaN) ||
                        BitConverter.SingleToUInt32Bits(unicodeNaN) != canonicalNaN ||
                        !float.TryParse(utf8NaN, out var unicodeUtf8NaN) ||
                        BitConverter.SingleToUInt32Bits(unicodeUtf8NaN) != canonicalNaN ||
                        BitConverter.SingleToUInt32Bits(float.Parse("-NaN")) != canonicalNaN)
                    {
                        return 10;
                    }

                    var formatFailures = 0;
                    try { _ = Convert.ToInt32("2147483648x"); }
                    catch (FormatException) { formatFailures++; }
                    try { _ = Convert.ToUInt64("18446744073709551616x"); }
                    catch (FormatException) { formatFailures++; }
                    if (formatFailures != 2)
                    {
                        return 11;
                    }

                    var noneResult = 123;
                    byte invalidByteResult = 123;
                    var binaryUtf8 = new byte[] { (byte)'1', (byte)'0', (byte)'1' };
                    var signedHexUtf8 = new byte[] { (byte)'F', (byte)'F' };
                    var partialUtf8 = new byte[] { (byte)'+', (byte)'1', (byte)'x', (byte)'+' };
                    if (int.Parse("42", System.Globalization.NumberStyles.None) != 42 ||
                        int.TryParse(" +42 ", System.Globalization.NumberStyles.None, null, out noneResult) ||
                        noneResult != 0 ||
                        int.Parse("101", System.Globalization.NumberStyles.BinaryNumber) != 5 ||
                        short.Parse("FFFF".AsSpan(), System.Globalization.NumberStyles.HexNumber, null) != -1 ||
                        int.Parse("80000000", System.Globalization.NumberStyles.HexNumber) != int.MinValue ||
                        long.Parse("FFFFFFFFFFFFFFFF", System.Globalization.NumberStyles.HexNumber) != -1L ||
                        sbyte.Parse("FF", System.Globalization.NumberStyles.HexNumber) != -1 ||
                        sbyte.Parse("80", System.Globalization.NumberStyles.HexNumber) != sbyte.MinValue ||
                        sbyte.Parse(signedHexUtf8, System.Globalization.NumberStyles.HexNumber, null) != -1 ||
                        byte.Parse(binaryUtf8, System.Globalization.NumberStyles.BinaryNumber, null) != 5 ||
                        byte.Parse("-0", System.Globalization.NumberStyles.Integer) != 0 ||
                        byte.Parse("1,,2,", System.Globalization.NumberStyles.Number) != 12 ||
                        byte.Parse("2550e-1", System.Globalization.NumberStyles.Float) != byte.MaxValue ||
                        byte.TryParse("\u200042\u2000", out _) ||
                        sbyte.TryParse("\u200042\u2000", out _) ||
                        byte.TryParse("12x", out invalidByteResult) ||
                        invalidByteResult != 0 ||
                        ushort.Parse("-0", System.Globalization.NumberStyles.Integer) != 0 ||
                        ushort.Parse("1,,234,", System.Globalization.NumberStyles.Number) != 1234 ||
                        uint.Parse("123 +", System.Globalization.NumberStyles.Number) != 123U ||
                        ulong.Parse(binaryUtf8, System.Globalization.NumberStyles.BinaryNumber, null) != 5UL ||
                        long.Parse("1e2", System.Globalization.NumberStyles.AllowExponent) != 100L ||
                        uint.Parse("1.00", System.Globalization.NumberStyles.AllowDecimalPoint) != 1U ||
                        !Int128.TryParsePartial("+1x+".AsSpan(), System.Globalization.NumberStyles.Number, null,
                            out var signedPartial, out var signedConsumed) ||
                        signedPartial != 1 || signedConsumed != 2 ||
                        !UInt128.TryParsePartial(partialUtf8, System.Globalization.NumberStyles.Number, null,
                            out var unsignedPartial, out var unsignedConsumed) ||
                        unsignedPartial != 1 || unsignedConsumed != 2 ||
                        !Int128.TryParsePartial("1+x".AsSpan(), System.Globalization.NumberStyles.Number, null,
                            out signedPartial, out signedConsumed) ||
                        signedPartial != 1 || signedConsumed != 2 ||
                        !Int128.TryParsePartial("1e+x".AsSpan(), System.Globalization.NumberStyles.AllowExponent, null,
                            out signedPartial, out signedConsumed) ||
                        signedPartial != 1 || signedConsumed != 1 ||
                        !Int128.TryParsePartial("1.0,x".AsSpan(), System.Globalization.NumberStyles.Number, null,
                            out signedPartial, out signedConsumed) ||
                        signedPartial != 1 || signedConsumed != 3 ||
                        UInt128.TryParsePartial("-1x".AsSpan(), System.Globalization.NumberStyles.Integer, null,
                            out _, out var failedConsumed) ||
                        failedConsumed != 0 ||
                        uint.TryParse("-0.0", System.Globalization.NumberStyles.Number, null, out _) ||
                        UInt128.TryParse("-0.0", System.Globalization.NumberStyles.Number, null, out _) ||
                        !decimal.TryParse("123 -", out var trailingSignDecimal) ||
                        trailingSignDecimal != -123m)
                    {
                        return 12;
                    }

                    var styledExceptions = 0;
                    try { _ = short.Parse("10000x", System.Globalization.NumberStyles.HexNumber); }
                    catch (FormatException) { styledExceptions++; }
                    try { _ = short.Parse("10000", System.Globalization.NumberStyles.HexNumber); }
                    catch (OverflowException) { styledExceptions++; }
                    try { _ = int.Parse((string)null, (System.Globalization.NumberStyles)int.MaxValue); }
                    catch (ArgumentException) { styledExceptions++; }
                    try { _ = byte.Parse("999999999999999999999999", System.Globalization.NumberStyles.Integer); }
                    catch (OverflowException) { styledExceptions++; }
                    if (styledExceptions != 4 ||
                        uint.TryParse("1.01", System.Globalization.NumberStyles.AllowDecimalPoint, null, out _))
                    {
                        return 13;
                    }

                    try
                    {
                        _ = decimal.Parse("1e1000");
                        return 7;
                    }
                    catch (FormatException)
                    {
                    }

                    try
                    {
                        _ = decimal.Parse("79,228,162,514,264,337,593,543,950,336");
                        return 8;
                    }
                    catch (OverflowException)
                    {
                    }

                    try
                    {
                        _ = Convert.ToSByte((string)null, null);
                        return 5;
                    }
                    catch (ArgumentNullException)
                    {
                    }

                    return 42;
                }
            }
            """,
            "DefaultParserContracts.EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(42, result);
    }

    [Fact]
    public void CompilerExecutesIntegerBitwiseShiftDivisionAndRemainderOperations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NumericOperationsFixture",
            """
            namespace NumericOperationsFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = input + 22;
                    var right = input - 38;
                    var signed = (left & 63) | 2;
                    signed ^= 3;
                    signed = (signed << 2) >> 1;
                    var unsigned = (uint)left;
                    var quotient = unsigned / (uint)right;
                    var remainder = unsigned % (uint)right;
                    var complement = ~input;
                    var negated = -input;
                    return signed + (int)quotient + (int)remainder
                        + (complement ^ negated);
                }
            }
            """);

        // The remainder follows a division of the same values, which would
        // otherwise give it its quotient; the remainder itself is under test.
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NumericOperationsFixture.EntryPoint",
            "Run",
            [],
            OptimizationOverrides: new WasmOptimizationOverrides(QuotientReuse: ManagedQuotientReuse.Disabled)));
        var operations = result.Program.Methods.Values
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operation)
            .ToHashSet();

        Assert.Contains(CilOperation.BitwiseAnd, operations);
        Assert.Contains(CilOperation.BitwiseOr, operations);
        Assert.Contains(CilOperation.BitwiseXor, operations);
        Assert.Contains(CilOperation.ShiftLeft, operations);
        Assert.Contains(CilOperation.ShiftRightSigned, operations);
        Assert.Contains(CilOperation.DivideUnsigned, operations);
        Assert.Contains(CilOperation.RemainderUnsigned, operations);
        Assert.Contains(CilOperation.Negate, operations);
        Assert.Contains(CilOperation.OnesComplement, operations);
        Assert.Equal(142, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesInt64SignedDivisionAndRemainder()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "Int64DivisionFixture",
            """
            namespace Int64DivisionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = (long)input * 4_294_967_296L + 17;
                    var divisor = (long)input + 2;
                    return (int)(value / divisor) + (int)(value % divisor);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Int64DivisionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(unchecked((int)(((long)41 * 4_294_967_296L + 17) / 43)) +
                     (int)(((long)41 * 4_294_967_296L + 17) % 43),
            ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerProducesManagedIntegerArithmeticExceptions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "IntegerExceptionFixture",
            """
            namespace IntegerExceptionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    try
                    {
                        _ = input / (input - input);
                    }
                    catch (System.DivideByZeroException)
                    {
                        result |= 1;
                    }

                    try
                    {
                        _ = int.MinValue / (input - 42);
                    }
                    catch (System.OverflowException)
                    {
                        result |= 2;
                    }

                    try
                    {
                        _ = checked(input + int.MaxValue);
                    }
                    catch (System.OverflowException)
                    {
                        result |= 4;
                    }

                    try
                    {
                        _ = checked(uint.MaxValue + (uint)input);
                    }
                    catch (System.OverflowException)
                    {
                        result |= 8;
                    }

                    try
                    {
                        _ = checked(0U - (uint)input);
                    }
                    catch (System.OverflowException)
                    {
                        result |= 16;
                    }

                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "IntegerExceptionFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(ManagedExceptionKind.DivideByZero, result.Program.ImplicitExceptions);
        Assert.Contains(ManagedExceptionKind.Overflow, result.Program.ImplicitExceptions);
        Assert.Equal(31, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesFloatingPointDivisionRemainderAndNegationWithoutExceptions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "FloatingPointFixture",
            """
            namespace FloatingPointFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = (double)input + 0.5;
                    var divided = left / 2.0;
                    var remainder = left % 4.0;
                    var infinity = left / 0.0;
                    var nan = 0.0 / 0.0;
                    var singleNan = 0.0f / 0.0f;
                    return (int)(divided + remainder - -1.0)
                        + (infinity > 0.0 ? 1 : 0)
                        + (nan != nan ? 1 : 0)
                        + (!(nan >= 0.0) ? 2 : 0)
                        + (!(nan <= 0.0) ? 4 : 0)
                        + (!(singleNan >= 0.0f) ? 8 : 0)
                        + (!(singleNan <= 0.0f) ? 16 : 0);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FloatingPointFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(55, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesNarrowAndCheckedNumericConversions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NumericConversionFixture",
            """
            namespace NumericConversionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = (int)(byte)(input + 256);
                    result += (byte)checked(input);
                    result += (ushort)checked(input);
                    result += (int)checked((uint)input);
                    result += (int)checked((long)(uint)input);
                    try
                    {
                        _ = checked((byte)(input + 256));
                    }
                    catch (System.OverflowException)
                    {
                        result += 1;
                    }
                    try
                    {
                        _ = checked((uint)-input);
                    }
                    catch (System.OverflowException)
                    {
                        result += 2;
                    }
                    try
                    {
                        _ = checked((int)(double)int.MaxValue + input);
                    }
                    catch (System.OverflowException)
                    {
                        result += 4;
                    }
                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NumericConversionFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(ManagedExceptionKind.Overflow, result.Program.ImplicitExceptions);
        Assert.Equal(212, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompilerExecutesCompleteSafeCheckedConversionMatrix(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CheckedConversionMatrixFixture",
            """
            namespace CheckedConversionMatrixFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var signed = (long)input;
                    var unsigned = (ulong)(uint)input;
                    var result = checked((sbyte)signed) + checked((byte)signed);
                    result += checked((short)signed) + checked((ushort)signed);
                    result += checked((int)signed) + (int)checked((uint)signed);
                    result += (int)checked((long)signed) + (int)checked((ulong)signed);
                    result += checked((sbyte)unsigned) + checked((byte)unsigned);
                    result += checked((short)unsigned) + checked((ushort)unsigned);
                    result += checked((int)unsigned) + (int)checked((uint)unsigned);
                    result += (int)checked((long)unsigned) + (int)checked((ulong)unsigned);
                    result += (int)checked((nint)signed) + (int)checked((nuint)signed);
                    result += (int)checked((nint)unsigned) + (int)checked((nuint)unsigned);
                    result += (int)checked((long)((double)input + 0.75));

                    var single = -(float)input - 0.5f;
                    result += (int)(single % 4.0f);
                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CheckedConversionMatrixFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(860, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            41,
            target,
            result.StaticDataEnd));
    }
}
