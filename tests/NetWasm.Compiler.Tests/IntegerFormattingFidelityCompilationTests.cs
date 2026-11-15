// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class IntegerFormattingFidelityCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void IntegerFormatsAndProvidersMatchDesktop(bool optimize, WasmTarget target)
    {
        string[] formats = ["", "G", "G3", "D", "D45", "X", "x40", "B", "B140",
            "N2", "F1", "C", "P1", "E3", "0.00", "#,##0;(#,##0);'zero'", "'µ'0'‰'"];
        var methods = new StringBuilder();
        var calls = new StringBuilder();
        Add<sbyte>("sbyte", [sbyte.MinValue, -1, 0, 1, sbyte.MaxValue]);
        Add<byte>("byte", [0, 1, byte.MaxValue]);
        Add<short>("short", [short.MinValue, -1, 0, 1, short.MaxValue]);
        Add<ushort>("ushort", [0, 1, ushort.MaxValue]);
        Add<int>("int", [int.MinValue, -1, 0, 1, int.MaxValue]);
        Add<uint>("uint", [0, 1, uint.MaxValue]);
        Add<long>("long", [long.MinValue, -1, 0, 1, long.MaxValue]);
        Add<ulong>("ulong", [0, 1, ulong.MaxValue]);
        Add<Int128>("Int128", [Int128.MinValue, -1, 0, 1, Int128.MaxValue]);
        Add<UInt128>("UInt128", [0, 1, UInt128.MaxValue]);
        Add<nint>("nint", [int.MinValue, -1, 0, 1, int.MaxValue], native: true);
        Add<nuint>("nuint", [0, 1, uint.MaxValue], native: true);
        var source = $$"""
            using System;
            using System.Globalization;
            using System.Text;
            namespace IntegerFormattingFidelityFixture;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{calls}}
                    return 1;
                }
                private sealed class Probe : IFormatProvider
                {
                    public int Calls;
                    public bool Throw;
                    public object? GetFormat(Type? type)
                    {
                        Calls++;
                        if (Throw) throw new InvalidOperationException();
                        return NumberFormatInfo.InvariantInfo;
                    }
                }
                {{methods}}
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("IntegerFormattingFidelityFixture", source)
            : assets.CompileSource("IntegerFormattingFidelityFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "IntegerFormattingFidelityFixture.EntryPoint", "Run", [], Target: target));
        Assert.Equal(1, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));

        void Add<T>(string type, T[] values, bool native = false) where T : IFormattable
        {
            var literals = values.Select(value =>
            {
                var text = value.ToString(null, CultureInfo.InvariantCulture);
                var bits = text.StartsWith('-') ? unchecked((UInt128)Int128.Parse(text, CultureInfo.InvariantCulture)) : UInt128.Parse(text, CultureInfo.InvariantCulture);
                return string.Create(CultureInfo.InvariantCulture,
                    $"unchecked(({type})new UInt128({(ulong)(bits >> 64)}UL, {(ulong)bits}UL))");
            });
            var expected = new List<string>();
            var providerCounts = new List<int>();
            foreach (var value in values)
            {
                foreach (var format in formats)
                {
                    var probe = new OracleProvider();
                    // Match the guest pointer width when constructing the desktop oracle.
                    IFormattable oracle = native && target == WasmTarget.Wasm32
                        ? type == "nint" ? (int)(nint)(object)value! : (IFormattable)(uint)(nuint)(object)value!
                        : value;
                    expected.Add(oracle.ToString(format, probe));
                    providerCounts.Add(probe.Calls);
                }
            }
            calls.AppendLine(CultureInfo.InvariantCulture, $"if (!Check_{type}()) return -1;");
            methods.AppendLine(CultureInfo.InvariantCulture, $$"""
                private static bool Check_{{type}}()
                {
                    {{type}}[] values = [{{string.Join(",", literals)}}];
                    string[] formats = {{JsonSerializer.Serialize(formats)}};
                    string[] expected = {{JsonSerializer.Serialize(expected)}};
                    int[] expectedCalls = [{{string.Join(",", providerCounts)}}];
                    Span<char> chars = stackalloc char[256];
                    Span<byte> bytes = stackalloc byte[512];
                    var index = 0;
                    foreach (var value in values)
                    {
                        foreach (var format in formats)
                        {
                            var probe = new Probe();
                            var text = expected[index];
                            if (value.ToString(format, probe) != text || probe.Calls != expectedCalls[index]) return false;
                            probe.Calls = 0;
                            if (!value.TryFormat(chars, out var written, format, probe) || chars[..written].ToString() != text || probe.Calls != expectedCalls[index]) return false;
                            probe.Calls = 0;
                            if (!value.TryFormat(bytes, out var byteCount, format, probe) || Encoding.UTF8.GetString(bytes[..byteCount]) != text || probe.Calls != expectedCalls[index]) return false;
                            chars.Fill('!');
                            if (value.TryFormat(chars[..(text.Length - 1)], out written, format, probe) || written != 0 || chars[0] != '!') return false;
                            bytes.Fill(255);
                            if (value.TryFormat(bytes[..(byteCount - 1)], out written, format, probe) || written != 0 || bytes[0] != 255) return false;
                            probe.Throw = true;
                            try
                            {
                                _ = value.ToString(format, probe);
                                if (expectedCalls[index] != 0) return false;
                            }
                            catch (InvalidOperationException)
                            {
                                if (expectedCalls[index] == 0) return false;
                            }
                            index++;
                        }
                        var defaultProbe = new Probe();
                        if (value.ToString(defaultProbe) != expected[index - formats.Length] || defaultProbe.Calls != expectedCalls[index - formats.Length]) return false;
                        try { _ = value.ToString("Q", null); return false; }
                        catch (FormatException) { }
                    }
                    return true;
                }
                """);
        }
    }

    private sealed class OracleProvider : IFormatProvider
    {
        public int Calls { get; private set; }

        public object? GetFormat(Type? formatType)
        {
            Calls++;
            return NumberFormatInfo.InvariantInfo;
        }
    }
}
