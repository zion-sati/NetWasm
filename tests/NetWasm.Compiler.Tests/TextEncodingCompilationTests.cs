using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class TextEncodingCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesHexStringConstructionFamily(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "HexStringFixture",
            """
            using System;

            namespace HexStringFixture;

            public static class EntryPoint
            {
                public static int Run(int operation)
                {
                    if (operation == -1)
                    {
                        return Run(0) == 0
                            && Run(1) == 0
                            && Run(2) == 0
                            && Run(3) == 0
                                ? 0
                                : 1;
                    }

                    string output;
                    if (operation == 0)
                    {
                        output = new string(new[] { '0', '1', '2', '3', 'A', 'B', 'C', 'D' });
                    }
                    else if (operation == 1)
                    {
                        var characters = new[] { '0', '1', '2', '3', 'A', 'B', 'C', 'D' };
                        output = new Span<char>(characters).ToString();
                    }
                    else if (operation == 2)
                    {
                        Span<char> characters = stackalloc char[8];
                        characters[0] = '0';
                        characters[1] = '1';
                        characters[2] = '2';
                        characters[3] = '3';
                        characters[4] = 'A';
                        characters[5] = 'B';
                        characters[6] = 'C';
                        characters[7] = 'D';
                        output = characters.ToString();
                    }
                    else
                    {
                        output = Convert.ToHexString(new byte[] { 0x01, 0x23, 0xAB, 0xCD });
                    }

                    if (output.Length != 8)
                    {
                        return 1;
                    }

                    if (output[0] != '0')
                    {
                        return 2;
                    }

                    if (output[4] != 'A')
                    {
                        return 3;
                    }

                    return output[7] == 'D' ? 0 : 4;
                }
            }
            """,
            "HexStringFixture.EntryPoint",
            optimize,
            target,
            -1,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesRuneDecodeBoundaryMatrix(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Buffers;
            using System.Text;

            namespace RuneDecodeFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var replacement = Rune.ReplacementChar.Value;
                    if (!CheckUtf8([], OperationStatus.NeedMoreData, replacement, 0)) return 101;
                    if (!CheckUtf8([0xF0], OperationStatus.NeedMoreData, replacement, 1)) return 102;
                    if (!CheckUtf8([0xF0, 0x9F], OperationStatus.NeedMoreData, replacement, 2)) return 103;
                    if (!CheckUtf8([0xF0, 0x9F, 0x98], OperationStatus.NeedMoreData, replacement, 3)) return 104;
                    if (!CheckUtf8([0xE1, 0x80, 0x41], OperationStatus.InvalidData, replacement, 2)) return 105;
                    if (!CheckUtf8([0xE0, 0x80, 0x80], OperationStatus.InvalidData, replacement, 1)) return 106;
                    if (!CheckUtf8([0xED, 0xA0, 0x80], OperationStatus.InvalidData, replacement, 1)) return 107;
                    if (!CheckUtf8([0xF4, 0x90, 0x80, 0x80], OperationStatus.InvalidData, replacement, 1)) return 108;
                    if (!CheckUtf8([0xF0, 0x9F, 0x98, 0x80], OperationStatus.Done, 0x1F600, 4)) return 109;
                    if (!CheckLastUtf8([], OperationStatus.NeedMoreData, replacement, 0)) return 110;
                    if (!CheckLastUtf8([0xF0, 0x9F, 0x98], OperationStatus.NeedMoreData, replacement, 3)) return 111;
                    if (!CheckLastUtf8([0xF0, 0x9F, 0x98, 0x80], OperationStatus.Done, 0x1F600, 4)) return 112;
                    if (!CheckLastUtf8([0x41, 0x80], OperationStatus.InvalidData, replacement, 1)) return 113;
                    if (!CheckUtf16([], OperationStatus.NeedMoreData, replacement, 0)) return 114;
                    if (!CheckUtf16(['\uD83D'], OperationStatus.NeedMoreData, replacement, 1)) return 115;
                    if (!CheckUtf16(['\uD83D', '\uDE00'], OperationStatus.Done, 0x1F600, 2)) return 116;
                    if (!CheckUtf16(['\uD83D', 'A'], OperationStatus.InvalidData, replacement, 1)) return 117;
                    if (!CheckUtf16(['\uDE00'], OperationStatus.InvalidData, replacement, 1)) return 118;
                    if (!CheckLastUtf16([], OperationStatus.NeedMoreData, replacement, 0)) return 119;
                    if (!CheckLastUtf16(['\uD83D'], OperationStatus.NeedMoreData, replacement, 1)) return 120;
                    if (!CheckLastUtf16(['\uD83D', '\uDE00'], OperationStatus.Done, 0x1F600, 2)) return 121;
                    if (!CheckLastUtf16(['\uDE00'], OperationStatus.InvalidData, replacement, 1)) return 122;
                    return input;
                }

                private static bool CheckUtf8(
                    byte[] source,
                    OperationStatus expectedStatus,
                    int expectedValue,
                    int expectedConsumed)
                {
                    var status = Rune.DecodeFromUtf8(source, out var rune, out var consumed);
                    return status == expectedStatus &&
                        rune.Value == expectedValue &&
                        consumed == expectedConsumed;
                }

                private static bool CheckLastUtf8(
                    byte[] source,
                    OperationStatus expectedStatus,
                    int expectedValue,
                    int expectedConsumed)
                {
                    var status = Rune.DecodeLastFromUtf8(source, out var rune, out var consumed);
                    return status == expectedStatus &&
                        rune.Value == expectedValue &&
                        consumed == expectedConsumed;
                }

                private static bool CheckUtf16(
                    char[] source,
                    OperationStatus expectedStatus,
                    int expectedValue,
                    int expectedConsumed)
                {
                    var status = Rune.DecodeFromUtf16(source, out var rune, out var consumed);
                    return status == expectedStatus &&
                        rune.Value == expectedValue &&
                        consumed == expectedConsumed;
                }

                private static bool CheckLastUtf16(
                    char[] source,
                    OperationStatus expectedStatus,
                    int expectedValue,
                    int expectedConsumed)
                {
                    var status = Rune.DecodeLastFromUtf16(source, out var rune, out var consumed);
                    return status == expectedStatus &&
                        rune.Value == expectedValue &&
                        consumed == expectedConsumed;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "RuneDecodeFixture",
            source,
            "RuneDecodeFixture.EntryPoint",
            optimize,
            target,
            37,
            []));

        Assert.Equal(37, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesBase64AndHexadecimalRoundTrips(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "BinaryTextFixture",
            """
            using System;

            namespace BinaryTextFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (input == 0)
                    {
                        return Run(1) == 8
                            && Run(2) == 5
                            && Run(3) == 10
                            && Run(4) == 5
                            && Run(5) == 1
                                ? 0
                                : 1;
                    }

                    byte[] source = [0, 1, 2, 254, 255];

                    if (input == 1)
                    {
                        return System.Convert.ToBase64String(source).Length;
                    }

                    if (input == 2)
                    {
                        return System.Convert.FromBase64String(
                            System.Convert.ToBase64String(source)).Length;
                    }

                    if (input == 3)
                    {
                        return System.Convert.ToHexString(source).Length;
                    }

                    if (input == 4)
                    {
                        return System.Convert.FromHexString(
                            System.Convert.ToHexString(source)).Length;
                    }

                    var encoded = System.Convert.ToBase64String(source);
                    var decoded = new byte[source.Length];
                    var status = System.Buffers.Text.Base64.DecodeFromChars(
                        encoded.AsSpan(),
                        decoded,
                        out var consumed,
                        out var written);
                    return status == System.Buffers.OperationStatus.Done &&
                        consumed == encoded.Length &&
                        written == source.Length &&
                        decoded[0] == source[0] &&
                        decoded[1] == source[1] &&
                        decoded[2] == source[2] &&
                        decoded[3] == source[3] &&
                        decoded[4] == source[4]
                            ? 1
                            : 0;
                }
            }
            """,
            "BinaryTextFixture.EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesUnicodeCodecRuneAndFallbackBoundaries(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Buffers;
            using System.Text;

            namespace UnicodeTextFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    const string source = "AΩ😀";
                    var utf8 = Encoding.UTF8.GetBytes(source);
                    var utf16 = Encoding.Unicode.GetBytes(source);
                    var utf32 = Encoding.UTF32.GetBytes(source);
                    var runeCount = 0;
                    var scalarTotal = 0;
                    foreach (var rune in source.EnumerateRunes())
                    {
                        runeCount++;
                        scalarTotal += rune.Value;
                    }

                    var status = Rune.DecodeFromUtf8(
                        new byte[] { 0xC0, 0xAF },
                        out var replacement,
                        out var consumed);
                    try
                    {
                        _ = new UTF8Encoding(false, true).GetString(
                            new byte[] { 0xC0, 0xAF });
                        return 0;
                    }
                    catch (DecoderFallbackException)
                    {
                    }

                    var decoder = Encoding.UTF8.GetDecoder();
                    byte[] firstBytes = [(byte)'h', 0xc3];
                    byte[] secondBytes = [0xa9];
                    var firstChars = new char[1];
                    var secondChars = new char[1];
                    var firstCount = decoder.GetCharCount(firstBytes.AsSpan(), false);
                    var firstWritten = decoder.GetChars(
                        firstBytes.AsSpan(), firstChars.AsSpan(), false);
                    var secondCount = decoder.GetCharCount(secondBytes.AsSpan(), true);
                    var secondWritten = decoder.GetChars(
                        secondBytes.AsSpan(), secondChars.AsSpan(), true);

                    return Encoding.UTF8.GetString(utf8) == source
                        && Encoding.Unicode.GetString(utf16) == source
                        && Encoding.UTF32.GetString(utf32) == source
                        && runeCount == 3
                        && scalarTotal == 129514
                        && status == OperationStatus.InvalidData
                        && replacement == Rune.ReplacementChar
                        && consumed == 1
                        && firstCount == 1
                        && firstWritten == 1
                        && firstChars[0] == 'h'
                        && secondCount == 1
                        && secondWritten == 1
                        && secondChars[0] == '\u00e9'
                        ? input
                        : 0;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "UnicodeTextFixture",
            source,
            "UnicodeTextFixture.EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(7, observed);
    }
}
