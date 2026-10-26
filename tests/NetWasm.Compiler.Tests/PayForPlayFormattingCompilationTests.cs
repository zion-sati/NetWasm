using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Formatting is pay-for-play: a program pays only for the formatting it uses.
// Integer formatting must not build floating-point or culture state, ToString()
// without a format must not reach the format string parser and formatter, and
// interpolation must cost little more than the values it formats (issue #118).
public sealed class PayForPlayFormattingCompilationTests
{
    private static readonly string[] FormatStringMachinery =
    [
        "NumberToStringFormatInvariant",
        "ParseInvariantFormatSpecifier",
        "FindInvariantFormatSection",
        "FormatInvariantExponent",
    ];

    [Theory]
    [InlineData("(input - 7).ToString()")]
    [InlineData("((long)input - 7).ToString()")]
    [InlineData("((uint)input).ToString()")]
    public void IntegerFormattingBuildsNoFloatingOrCultureState(string expression)
    {
        var reached = ReachedMethods(expression);

        Assert.DoesNotContain(reached, method => method.Contains("DiyFp128", StringComparison.Ordinal));
        Assert.DoesNotContain(reached, method => method.Contains("Tables::", StringComparison.Ordinal));
        Assert.DoesNotContain(reached, method => method.Contains("FormatPatterns::", StringComparison.Ordinal));
        Assert.DoesNotContain(reached, method => method.StartsWith("System.Globalization.", StringComparison.Ordinal));
        Assert.Contains(reached, method => method.StartsWith("System.Number::", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("(input + 0.5f).ToString()")]
    [InlineData("(input + 0.5).ToString()")]
    [InlineData("((System.Half)(input + 0.5f)).ToString()")]
    [InlineData("(input + 0.5).ToString(System.Globalization.CultureInfo.InvariantCulture)")]
    public void FloatingToStringWithoutAFormatReachesNoFormatStringMachinery(string expression)
    {
        var reached = ReachedMethods(expression);

        Assert.DoesNotContain(reached, method => FormatStringMachinery.Any(name =>
            method.EndsWith("::" + name, StringComparison.Ordinal)));
        Assert.DoesNotContain(reached, method => method.Contains("CultureData", StringComparison.Ordinal));
    }

    [Fact]
    public void FloatingToStringWithAFormatStillReachesTheFormatter()
    {
        var reached = ReachedMethods("(input + 0.5f).ToString(\"0.00\")");

        Assert.Contains(reached, method => method.EndsWith("::NumberToStringFormatInvariant", StringComparison.Ordinal));
    }

    private static readonly string[] FloatingFormatting =
    [
        "System.Number::FormatSingle",
        "System.Number::FormatDouble",
        "System.Number::FormatFloat",
        "System.Number::AppendShortestFloating",
    ];

    // Every interpolated string handler formats its holes the same way, so a
    // hole costs its own type's formatter and nothing else.
    [Theory]
    [InlineData("$\"i={input}\"", false, false)]
    [InlineData("$\"i={input,5} l={(long)input,-4} f={input + 0.5f}\"", true, false)]
    [InlineData("new System.Text.StringBuilder().Append($\"i={input,4}\").ToString()", false, true)]
    [InlineData("(new char[16].AsSpan().TryWrite($\"i={input,4}\", out var written) ? written : -1).ToString()", false, false)]
    [InlineData("(System.Text.Unicode.Utf8.TryWrite(new byte[16], $\"i={input,4}\", out var written) ? written : -1).ToString()", false, false)]
    public void InterpolationReachesOnlyTheFormattersItsHolesNeed(string expression, bool floatingHole, bool usesStringBuilder)
    {
        var reached = ReachedMethods(expression);

        if (!usesStringBuilder)
            Assert.DoesNotContain(reached, method => method.StartsWith("System.Text.StringBuilder::", StringComparison.Ordinal));
        if (!floatingHole)
            Assert.DoesNotContain(reached, method => FloatingFormatting.Any(name => method.StartsWith(name, StringComparison.Ordinal)));
        Assert.DoesNotContain(reached, method => method.StartsWith("System.Decimal", StringComparison.Ordinal));
        Assert.DoesNotContain(reached, method => method.Contains("CultureData", StringComparison.Ordinal));
        Assert.DoesNotContain(reached, method => FormatStringMachinery.Any(name =>
            method.EndsWith("::" + name, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void FloatingFormattingMatchesDesktop(bool optimize, WasmTarget target)
    {
        var checks = new StringBuilder();
        var count = 0;
        float[] singles =
        [
            0f, -0f, 1f, -1f, 0.1f, 0.5f, 1f / 3f, 123456789f, 1e-7f, 1e21f, 3.4028235e38f, -3.4028235e38f,
            1.4e-45f, 1.17549435e-38f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 16777217f,
        ];
        double[] doubles =
        [
            0d, -0d, 1d, 0.1, 0.3, 1d / 3d, 123456789012345678d, 1e-7, 1e21, 1e-320, double.MaxValue, double.MinValue,
            double.Epsilon, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 2.2250738585072014E-308,
        ];
        string[] formats = ["R", "G", "G4", "E", "E2", "F2", "0.00", "#,##0.###"];
        foreach (var value in singles)
        {
            var literal = SingleLiteral(value);
            Check($"({literal}).ToString()", value.ToString(CultureInfo.InvariantCulture));
            Check($"$\"{{{literal}}}\"", value.ToString(CultureInfo.InvariantCulture));
            foreach (var format in formats)
            {
                Check($"({literal}).ToString(\"{format}\")", value.ToString(format, CultureInfo.InvariantCulture));
            }
        }
        foreach (var value in doubles)
        {
            var literal = DoubleLiteral(value);
            Check($"({literal}).ToString()", value.ToString(CultureInfo.InvariantCulture));
            foreach (var format in formats)
            {
                Check($"({literal}).ToString(\"{format}\")", value.ToString(format, CultureInfo.InvariantCulture));
            }
        }
        foreach (var value in new[] { 0.5f, -2.25f, 65504f, 6.1e-5f, float.NaN, float.NegativeInfinity })
        {
            Check($"((System.Half){SingleLiteral(value)}).ToString()",
                ((Half)value).ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(count, Execute(checks.ToString(), count, optimize, target));

        void Check(string expression, string expected)
        {
            count++;
            checks.AppendLine(CultureInfo.InvariantCulture,
                $$"""if ({{expression}} != {{Quote(expected)}}) return -{{count}};""");
        }
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InterpolationMatchesDesktop(bool optimize, WasmTarget target)
    {
        var count = 0;
        var checks = new StringBuilder();
        var expected = new Dictionary<string, string>
        {
            ["$\"i={input}\""] = $"i={42}",
            ["$\"[{input,5}][{input,-5}][{-input,3}]\""] = $"[{42,5}][{42,-5}][{-42,3}]",
            ["$\"{input:X4} {input:D6} {-input:N0} {input * 1000:#,##0}\""] =
                string.Create(CultureInfo.InvariantCulture, $"{42:X4} {42:D6} {-42:N0} {42 * 1000:#,##0}"),
            ["$\"{(long)input * -100000000000} {(ulong)input} {(short)-input} {(byte)input} {(sbyte)-input} {(ushort)input}\""] =
                string.Create(CultureInfo.InvariantCulture, $"{42L * -100000000000} {42UL} {(short)-42} {(byte)42} {(sbyte)-42} {(ushort)42}"),
            ["$\"{(nint)input} {(nuint)input} {input + 0.25f} {input / 8.0} {input + 0.5m}\""] =
                string.Create(CultureInfo.InvariantCulture, $"{(nint)42} {(nuint)42} {42 + 0.25f} {42 / 8.0} {42 + 0.5m}"),
            ["$\"{input + 0.5:F3}|{input + 0.5,8:E1}|{input + 0.5,-8:0.0}|\""] =
                string.Create(CultureInfo.InvariantCulture, $"{42 + 0.5:F3}|{42 + 0.5,8:E1}|{42 + 0.5,-8:0.0}|"),
            ["$\"{'c'} {true} {(string?)null}|{(object?)null}|{\"text\",6}|{new Point(1, 2)}|{new Point(3, 4):swap}\""] =
                "c True ||  text|1,2|4,3",
            ["$\"{new Plain()} {(object)input} {(object)(input + 0.5)}\""] = "plain 42 42.5",
            ["string.Create(new Bracketing(), $\"<{input}> {input:X} {\"s\"}\")"] = "<[42:]> [42:X] [s:]",
            ["string.Create(null, stackalloc char[4], $\"{input}{input}{input}{input}{input}{input}{input}{input}\")"] =
                string.Concat(Enumerable.Repeat("42", 8)),
            ["$\"{new string('x', 300)}{input}{new string('y', 300)}\""] = new string('x', 300) + "42" + new string('y', 300),
            ["Direct(input)"] = "a42b|42 ",
            ["new System.Text.StringBuilder(\"x\").Append($\"[{input,4}|{input:X}|{input + 0.5}]\").ToString()"] =
                string.Create(CultureInfo.InvariantCulture, $"x[{42,4}|{42:X}|{42 + 0.5}]"),
            ["new System.Text.StringBuilder().Append(new Bracketing(), $\"<{input}> {\"s\"}\").ToString()"] = "<[42:]> [s:]",
            ["new System.Text.StringBuilder().AppendFormat(\"{0,3}|{1:F1}|{2}|{3,-4}|\", input, input + 0.25, (object?)null, \"ab\").ToString()"] =
                string.Format(CultureInfo.InvariantCulture, "{0,3}|{1:F1}|{2}|{3,-4}|", 42, 42 + 0.25, null, "ab"),
            ["SpanWrite(input)"] = "True:[  42]:False:0:<[42:X]>",
            ["Utf8Write(input)"] = "True:8:\u00e942 |2A",
        };
        foreach (var (expression, value) in expected)
        {
            count++;
            checks.AppendLine(CultureInfo.InvariantCulture,
                $$"""if ({{expression}} != {{Quote(value)}}) return -{{count}};""");
        }
        const string Types = """
            readonly struct Point(int x, int y) : IFormattable
            {
                public override string ToString() => "unused";
                public string ToString(string? format, IFormatProvider? provider) =>
                    format == "swap" ? $"{y},{x}" : $"{x},{y}";
            }

            sealed class Plain
            {
                public override string ToString() => "plain";
            }

            sealed class Bracketing : IFormatProvider, ICustomFormatter
            {
                public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
                public string Format(string? format, object? arg, IFormatProvider? provider) => $"[{arg}:{format}]";
            }
            """;
        const string Helpers = """
            static string Direct(int input)
            {
                var handler = new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(2, 1);
                handler.AppendLiteral("a");
                handler.AppendFormatted(input);
                handler.AppendLiteral("b");
                var first = handler.ToStringAndClear();
                handler = new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(0, 1);
                handler.AppendFormatted(input, -3);
                return first + "|" + handler.Text.ToString();
            }

            static string SpanWrite(int input)
            {
                Span<char> buffer = stackalloc char[8];
                var fits = buffer.TryWrite($"[{input,4}]", out var written);
                var text = buffer.Slice(0, written).ToString();
                var tooSmall = buffer.Slice(0, 3).TryWrite($"[{input,4}]", out var none);
                var custom = buffer.TryWrite(new Bracketing(), $"<{input:X}>", out var customWritten);
                return $"{fits}:{text}:{tooSmall}:{none}:{(custom ? buffer.Slice(0, customWritten).ToString() : "-")}";
            }

            static string Utf8Write(int input)
            {
                Span<byte> buffer = stackalloc byte[16];
                var fits = System.Text.Unicode.Utf8.TryWrite(buffer, $"\u00e9{input,-3}|{input:X}", out var written);
                return $"{fits}:{written}:{System.Text.Encoding.UTF8.GetString(buffer.Slice(0, written))}";
            }
            """;

        Assert.Equal(count, Execute(checks.ToString(), count, optimize, target, Types, Helpers));
    }

    private static List<string> ReachedMethods(string expression)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "PayForPlayProbe",
            $$"""
            using System;
            public static class EntryPoint
            {
                public static int Run(int input) => {{expression}}.Length;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32));
        return
        [
            .. result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
                .Select(static body => $"{body.Method.DeclaringType.FullName}::{body.Method.Definition.Name}")
                .Distinct(StringComparer.Ordinal),
        ];
    }

    private static int Execute(
        string checks,
        int count,
        bool optimize,
        WasmTarget target,
        string types = "",
        string helpers = "")
    {
        using var assets = TestAssets.Create();
        var source = $$"""
            using System;
            using System.Linq;
            namespace PayForPlayFormattingFixture;
            {{types}}
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{checks}}
                    return {{count}};
                }
                {{helpers}}
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("PayForPlayFormattingFixture", source)
            : assets.CompileSource("PayForPlayFormattingFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "PayForPlayFormattingFixture.EntryPoint", "Run", [], Target: target));
        return ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd);
    }

    private static string SingleLiteral(float value) => float.IsNaN(value) ? "float.NaN"
        : float.IsPositiveInfinity(value) ? "float.PositiveInfinity"
        : float.IsNegativeInfinity(value) ? "float.NegativeInfinity"
        : $"BitConverter.Int32BitsToSingle({BitConverter.SingleToInt32Bits(value)})";

    private static string DoubleLiteral(double value) => double.IsNaN(value) ? "double.NaN"
        : double.IsPositiveInfinity(value) ? "double.PositiveInfinity"
        : double.IsNegativeInfinity(value) ? "double.NegativeInfinity"
        : $"BitConverter.Int64BitsToDouble({BitConverter.DoubleToInt64Bits(value)}L)";

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
