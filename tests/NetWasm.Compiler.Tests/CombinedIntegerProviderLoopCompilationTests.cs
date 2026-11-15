// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class CombinedIntegerProviderLoopCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void NestedProviderExceptionsInsideFormattingLoopsExecute(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            using System.Globalization;
            using System.Text;
            namespace ProviderLoopFixture;
            public static class EntryPoint
            {
                public static int Run(int input) => Check() ? 1 : 0;
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
                private static bool Check()
                {
                    int[] values = [-1, 0, 1];
                    string[] formats = ["G", "D"];
                    string[] expected = ["-1", "-1", "0", "0", "1", "1"];
                    int[] expectedCalls = [1, 1, 0, 0, 0, 0];
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

            }
            """;
        AssertExecutes(source, optimize, target);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void SmallerNestedExceptionLoopsPreserveEarlyReturnOwnership(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            namespace ProviderLoopFixture;
            public static class EntryPoint
            {
                public static int Run(int input) => Check() ? 1 : 0;
                private static bool Check()
                {
                    int[] values = [-1, 0, 1];
                    string[] formats = ["G", "D"];
                    foreach (var value in values)
                    {
                        foreach (var format in formats)
                        {
                            var probe = new Probe();
                            probe.Throw = true;
                            try
                            {
                                _ = value.ToString(format, probe);
                                if (value < 0) return false;
                            }
                            catch (InvalidOperationException)
                            {
                                if (value >= 0) return false;
                            }
                        }
                        try { _ = value.ToString("Q", null); return false; }
                        catch (FormatException) { }
                    }
                    return true;
                }
                private sealed class Probe : IFormatProvider
                {
                    public bool Throw;
                    public object? GetFormat(Type? type)
                    {
                        if (Throw) throw new InvalidOperationException();
                        return null;
                    }
                }
            }
            """;
        AssertExecutes(source, optimize, target);
    }

    private static void AssertExecutes(string source, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("ProviderLoopFixture", source)
            : assets.CompileSource("ProviderLoopFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "ProviderLoopFixture.EntryPoint", "Run", [], Target: target));
        Assert.Equal(1, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }
}
