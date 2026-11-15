// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class IntegerFormattingConsumerCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void Utf8FormattingMatchesDesktopIncludingPrecisionAndShortDestinations(bool optimize, WasmTarget target)
    {
        string[] integerFormats = ["", "G", "R", "D", "D20", "D99", "x", "X20", "N", "N0", "N3", "G2", "R1", "Q"];
        string[] realFormats = ["", "G", "F3", "E2", "Q"];
        var methods = new StringBuilder();
        var calls = new StringBuilder();
        Add<sbyte>("sbyte", [sbyte.MinValue, -1, 0, sbyte.MaxValue], integerFormats);
        Add<byte>("byte", [0, 1, byte.MaxValue], integerFormats);
        Add<short>("short", [short.MinValue, -1, 0, short.MaxValue], integerFormats);
        Add<ushort>("ushort", [0, 1, ushort.MaxValue], integerFormats);
        Add<int>("int", [int.MinValue, -1, 0, int.MaxValue], integerFormats);
        Add<uint>("uint", [0, 1, uint.MaxValue], integerFormats);
        Add<long>("long", [long.MinValue, -1, 0, long.MaxValue], integerFormats);
        Add<ulong>("ulong", [0, 1, ulong.MaxValue], integerFormats);
        Add<float>("float", [-1234.5f, 0, 0.125f], realFormats);
        Add<double>("double", [-1234.5, 0, 0.125], realFormats);
        Add<decimal>("decimal", [-1234.50m, 0, 0.125m], realFormats);
        AssertExecutes(methods, calls, optimize, target);

        void Add<T>(string type, T[] values, string[] formats) where T : struct, IFormattable
        {
            var literals = values.Select(value => type == "decimal"
                ? value.ToString(null, CultureInfo.InvariantCulture) + "m"
                : type is "float" or "double"
                    ? value.ToString("R", CultureInfo.InvariantCulture) + (type == "float" ? "f" : "d")
                    : value.ToString(null, CultureInfo.InvariantCulture) + (type == "ulong" ? "UL" : type == "long" ? "L" : ""));
            var expected = values.SelectMany(value => formats.Select(format => Utf8Oracle(value, StandardFormat.Parse(format))));
            calls.AppendLine(CultureInfo.InvariantCulture, $"if (!Check_{type}()) return -1;");
            methods.AppendLine(CultureInfo.InvariantCulture, $$"""
                private static bool Check_{{type}}()
                {
                    {{type}}[] values = [{{string.Join(",", literals)}}];
                    string[] formats = [{{string.Join(",", formats.Select(text => JsonSerializer.Serialize(text)))}}];
                    string[] expected = [{{string.Join(",", expected.Select(text => JsonSerializer.Serialize(text)))}}];
                    var buffer = new byte[256];
                    var index = 0;
                    foreach (var value in values)
                    {
                        foreach (var text in formats)
                        {
                            var format = StandardFormat.Parse(text);
                            var wanted = expected[index++];
                            try
                            {
                                if (!Utf8Formatter.TryFormat(value, buffer, out var written, format)) return false;
                                if (Encoding.UTF8.GetString(buffer.AsSpan(0, written)) != wanted) return false;
                                buffer.AsSpan().Fill((byte)0x5A);
                                if (Utf8Formatter.TryFormat(value, buffer.AsSpan(0, written - 1), out var shortWritten, format)) return false;
                                if (shortWritten != 0) return false;
                                for (var i = 0; i < buffer.Length; i++) if (buffer[i] != 0x5A) return false;
                            }
                            catch (FormatException) { if (wanted != "#format") return false; }
                            catch (NotSupportedException) { if (wanted != "#unsupported") return false; }
                        }
                    }
                    return true;
                }
                """);
        }
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void BaseConversionsMatchDesktopAtSignedWidthBoundaries(bool optimize, WasmTarget target)
    {
        var methods = new StringBuilder();
        var calls = new StringBuilder();
        Add<byte>("byte", [0, 1, byte.MaxValue], (value, radix) => Convert.ToString(value, radix));
        Add<short>("short", [short.MinValue, -1, 0, 1, short.MaxValue], (value, radix) => Convert.ToString(value, radix));
        Add<int>("int", [int.MinValue, -1, 0, 1, int.MaxValue], (value, radix) => Convert.ToString(value, radix));
        Add<long>("long", [long.MinValue, -1, 0, 1, long.MaxValue], (value, radix) => Convert.ToString(value, radix));
        AssertExecutes(methods, calls, optimize, target);

        void Add<T>(string type, T[] values, Func<T, int, string> oracle) where T : IFormattable
        {
            int[] bases = [2, 8, 10, 16];
            var literals = values.Select(value => value.ToString(null, CultureInfo.InvariantCulture) + (type == "long" ? "L" : ""));
            var expected = values.SelectMany(value => bases.Select(radix => oracle(value, radix)));
            calls.AppendLine(CultureInfo.InvariantCulture, $"if (!Check_{type}()) return -1;");
            methods.AppendLine(CultureInfo.InvariantCulture, $$"""
                private static bool Check_{{type}}()
                {
                    {{type}}[] values = [{{string.Join(",", literals)}}];
                    int[] bases = [2, 8, 10, 16];
                    string[] expected = [{{string.Join(",", expected.Select(text => JsonSerializer.Serialize(text)))}}];
                    var index = 0;
                    foreach (var value in values)
                        foreach (var radix in bases)
                            if (Convert.ToString(value, radix) != expected[index++]) return false;
                    try { Convert.ToString(values[0], 3); return false; }
                    catch (ArgumentException) { return true; }
                }
                """);
        }
    }

    private static void AssertExecutes(StringBuilder methods, StringBuilder calls, bool optimize, WasmTarget target)
    {
        var source = $$"""
            using System;
            using System.Buffers;
            using System.Buffers.Text;
            using System.Text;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{calls}}
                    return 1;
                }
                {{methods}}
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("IntegerFormattingConsumerFixture", source)
            : assets.CompileSource("IntegerFormattingConsumerFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));
        Assert.Equal(1, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    private static string Utf8Oracle<T>(T value, StandardFormat format) where T : struct
    {
        Span<byte> buffer = stackalloc byte[256];
        int written;
        try
        {
            var success = value switch
            {
                sbyte number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                byte number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                short number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                ushort number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                int number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                uint number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                long number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                ulong number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                float number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                double number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                decimal number => Utf8Formatter.TryFormat(number, buffer, out written, format),
                _ => throw new InvalidOperationException(),
            };
            Assert.True(success);
            return Encoding.UTF8.GetString(buffer[..written]);
        }
        catch (FormatException) { return "#format"; }
        catch (NotSupportedException) { return "#unsupported"; }
    }
}
