// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// An interpolated hole that formats a primitive integer as hexadecimal with a constant format writes what desktop .NET
// writes, through a string's handler or a StringBuilder's, with or without alignment, and carries no parser of item
// formats where the handler has no custom formatter. A handler that may have one keeps the handler's own formatting.
public sealed class InterpolatedHexHoleCompilationTests
{
    private const string Types = """
        public sealed class IntegerFormatter : IFormatProvider, ICustomFormatter
        {
            public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
            public string Format(string? format, object? arg, IFormatProvider? formatProvider) => arg is int i ? "#" + i + format : null!;
        }

        public static class Handlers
        {
            // A provider site and a plain one in one method, so a handler local may serve both.
            public static string Mixed(int value)
            {
                var builder = new System.Text.StringBuilder();
                builder.Append(new IntegerFormatter(), $"{value:X2}");
                builder.Append($"|{value:X2}");
                builder.Append(new IntegerFormatter(), $"|{value,5:x}");
                return builder.ToString();
            }

            // A handler passed by reference to a method that builds it again with a custom formatter.
            public static string Rebuilt(int value)
            {
                var handler = new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(0, 1);
                Rebuild(ref handler);
                handler.AppendFormatted(value, "X4");
                return handler.ToStringAndClear();
            }

            private static void Rebuild(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler handler)
            {
                handler.ToStringAndClear();
                handler = new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(0, 1, new IntegerFormatter());
            }

            // A provider local that starts as the invariant culture and may become a custom formatter.
            public static string Conditional(int value)
            {
                IFormatProvider provider = System.Globalization.CultureInfo.InvariantCulture;
                if (value > 40)
                {
                    provider = new IntegerFormatter();
                }
                return string.Create(provider, $"{value:X4}|{(byte)value:x2}");
            }

            public static string Decoded(byte[] bytes)
            {
                try
                {
                    return new System.Text.UTF8Encoding(false, true).GetString(bytes);
                }
                catch (System.Text.DecoderFallbackException exception)
                {
                    return exception.Message;
                }
            }
        }
        """;

    public static TheoryData<bool, WasmTarget> Targets => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void FormatsAsDesktopDoes(bool optimize, WasmTarget target)
    {
        var input = 42;
        var checks = new StringBuilder();
        var count = 0;
        Check("$\"[{(byte)input:X2}]\"", $"[{(byte)input:X2}]");
        Check("$\"{input:x}|{input:X8}|{(sbyte)-input:X}|{(short)-input:x4}|{-(long)input:X}\"",
            $"{input:x}|{input:X8}|{(sbyte)-input:X}|{(short)-input:x4}|{-(long)input:X}");
        Check("$\"{(ushort)input:X1}|{(uint)input:X0}|{(ulong)0:x}|{ulong.MaxValue - (ulong)input:X}|{int.MinValue:x}\"",
            $"{(ushort)input:X1}|{(uint)input:X0}|{(ulong)0:x}|{ulong.MaxValue - (ulong)input:X}|{int.MinValue:x}");
        Check("$\"[{input,6:X}][{input,-6:x2}][{(byte)input,1:X4}][{-input,-12:X}]\"",
            $"[{input,6:X}][{input,-6:x2}][{(byte)input,1:X4}][{-input,-12:X}]");
        Check("$\"{input:X99}|{new string('y', 300)}{(long)input,280:x}\"",
            $"{input:X99}|{new string('y', 300)}{(long)input,280:x}");
        Check("string.Create(System.Globalization.CultureInfo.InvariantCulture, $\"{input:X4}|{input,3:x}\")",
            string.Create(CultureInfo.InvariantCulture, $"{input:X4}|{input,3:x}"));
        Check("string.Create(null, $\"{input:X4}\")", string.Create(null, $"{input:X4}"));
        Check("string.Create(new IntegerFormatter(), $\"{input:X4}|{(byte)input:X2}|{input,6:x}\")",
            string.Create(new IntegerFormatter(), $"{input:X4}|{(byte)input:X2}|{input,6:x}"));
        Check("new System.Text.StringBuilder().Append($\"[{(byte)input:X2}]\").Append($\"{input,5:x}|{input,-4:X}|\").ToString()",
            new StringBuilder().Append(CultureInfo.InvariantCulture, $"[{(byte)input:X2}]").Append(CultureInfo.InvariantCulture, $"{input,5:x}|{input,-4:X}|").ToString());
        Check("new System.Text.StringBuilder(\"a\").Append(System.Globalization.CultureInfo.InvariantCulture, $\"{input:X3}\").ToString()",
            new StringBuilder("a").Append(CultureInfo.InvariantCulture, $"{input:X3}").ToString());
        Check("new System.Text.StringBuilder().Append(new IntegerFormatter(), $\"{input:X3}|{(byte)input:X2}\").ToString()",
            new StringBuilder().Append(new IntegerFormatter(), $"{input:X3}|{(byte)input:X2}").ToString());
        Check("Handlers.Mixed(input)", Mixed(input));
        Check("Handlers.Rebuilt(input)", Rebuilt(input));
        Check("Handlers.Conditional(input)", Conditional(input));
        // A native integer's digits are its target's width: four bytes on wasm32, eight on wasm64.
        var width = target == WasmTarget.Wasm64 ? 16 : 8;
        Check("$\"{(nint)(-input):X}|{(nuint)input:x4}|{(nuint)input,3:X}\"", new string('F', width - 2) + "D6|002a| 2A");
        // The decoder's message names the bytes it could not decode as upstream writes them.
        Check("Handlers.Decoded(new byte[] { 0x41, 0xFF, 0xC3 }).Contains(\"[FF]\") ? \"y\" : \"n\"", "y");

        Assert.Equal(count, Execute(checks.ToString(), count, optimize, target));

        void Check(string expression, string expected)
        {
            count++;
            checks.AppendLine(CultureInfo.InvariantCulture, $$"""if (({{expression}}) != {{Quote(expected)}}) return -{{count}};""");
        }
    }

    [Theory]
    [InlineData("$\"[{(byte)input:X2}]\"", true, false)]
    [InlineData("$\"{input,6:x}|{(long)input:X16}|{(nuint)input:x}\"", true, false)]
    [InlineData("new System.Text.StringBuilder().Append($\"[{(byte)input:X2}]\").ToString()", true, false)]
    [InlineData("new System.Text.StringBuilder().Append(System.Globalization.CultureInfo.InvariantCulture, $\"{input,4:x}\").ToString()", true, false)]
    [InlineData("string.Create(System.Globalization.CultureInfo.InvariantCulture, $\"{input:X2}\")", true, false)]
    // The decoder's message for bytes it cannot decode, upstream's $"[{b:X2}]" in a StringBuilder.
    [InlineData("Handlers.Decoded(new byte[] { 0x41, 0xFF })", true, false)]
    [InlineData("$\"{input:D4}\"", false, true)]
    [InlineData("string.Create(new IntegerFormatter(), $\"{input:X2}\")", false, true)]
    [InlineData("Handlers.Rebuilt(input)", false, true)]
    [InlineData("Handlers.Conditional(input)", false, true)]
    public void ConstantHexHolesReachNoFormatParser(string expression, bool rewritten, bool formatParser)
    {
        var reached = ReachedMethods($"public static int Run(int input) => ({expression}).Length;", optimize: true);

        Assert.Equal(rewritten, reached.Any(static method => method.StartsWith("System.Text.InterpolatedHexHoles::", StringComparison.Ordinal)));
        Assert.Equal(formatParser, reached.Any(static method => method is "System.Number::ParseFormatSpecifier" or "System.Number::FindSection"));
    }

    // Roslyn's Debug code builds a handler in its local as Release code does, so the rewrite reaches it too.
    [Fact]
    public void DebugCodeIsRewrittenAsReleaseCodeIs()
    {
        var reached = ReachedMethods(
            "public static int Run(int input) => ($\"[{(byte)input:X2}]\" + new System.Text.StringBuilder().Append($\"{input,3:x}\")).Length;",
            optimize: false);

        Assert.Contains("System.Text.InterpolatedHexHoles::AppendDefault", reached);
        Assert.Contains("System.Text.InterpolatedHexHoles::AppendBuilderAligned", reached);
        Assert.DoesNotContain(reached, static method => method is "System.Number::ParseFormatSpecifier" or "System.Number::FindSection");
    }

    private static string Mixed(int value)
    {
        var builder = new StringBuilder();
        builder.Append(new IntegerFormatter(), $"{value:X2}");
        builder.Append(CultureInfo.InvariantCulture, $"|{value:X2}");
        builder.Append(new IntegerFormatter(), $"|{value,5:x}");
        return builder.ToString();
    }

    private static string Conditional(int value)
    {
        IFormatProvider provider = CultureInfo.InvariantCulture;
        if (value > 40)
        {
            provider = new IntegerFormatter();
        }
        return string.Create(provider, $"{value:X4}|{(byte)value:x2}");
    }

    private static string Rebuilt(int value)
    {
        var handler = new DefaultInterpolatedStringHandler(0, 1);
        Rebuild(ref handler);
        handler.AppendFormatted(value, "X4");
        return handler.ToStringAndClear();
    }

    private static void Rebuild(ref DefaultInterpolatedStringHandler handler)
    {
        handler.ToStringAndClear();
        handler = new DefaultInterpolatedStringHandler(0, 1, new IntegerFormatter());
    }

    private static List<string> ReachedMethods(string run, bool optimize)
    {
        using var assets = TestAssets.Create();
        var source = $$"""
            using System;
            {{Types}}
            public static class EntryPoint
            {
                {{run}}
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("InterpolatedHexHoleProbe", source)
            : assets.CompileSource("InterpolatedHexHoleProbe", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32));
        return
        [
            .. result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
                .Select(static body => $"{body.Method.DeclaringType.FullName}::{body.Method.Definition.Name}")
                .Distinct(StringComparer.Ordinal),
        ];
    }

    private static int Execute(string checks, int count, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = $$"""
            using System;
            namespace InterpolatedHexHoleFixture;
            {{Types}}
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{checks}}
                    return {{count}};
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("InterpolatedHexHoleFixture", source)
            : assets.CompileSource("InterpolatedHexHoleFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "InterpolatedHexHoleFixture.EntryPoint", "Run", [], Target: target));
        return ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd);
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private sealed class IntegerFormatter : IFormatProvider, ICustomFormatter
    {
        public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
        public string Format(string? format, object? arg, IFormatProvider? formatProvider) => arg is int i ? "#" + i + format : null!;
    }
}
