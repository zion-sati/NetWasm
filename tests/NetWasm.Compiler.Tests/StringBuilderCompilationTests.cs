using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class StringBuilderCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesStringsAcrossConstructionAndBuilderCopies(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Buffers;
            using System.Text;

            namespace StringCopyFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var literal = "x";
                    var created = new string('z', 3);
                    var builder = new StringBuilder();
                    builder.Append("abc");
                    var built = builder.ToString();

                    var observed = 0;
                    if (literal.Length == 1) observed |= 1;
                    if (literal[0] == 'x') observed |= 2;
                    if (created.Length == 3) observed |= 4;
                    if (created[0] == 'z' && created[2] == 'z') observed |= 8;
                    if (builder.Length == 3) observed |= 16;
                    if (built.Length == 3) observed |= 32;
                    if (built == "abc") observed |= 64;

                    var characters = new[] { 'A', 'é', '\ud83d', '\ude00' };
                    var unicodeBuilder = new StringBuilder("prefix");
                    unicodeBuilder.Append(characters, 0, characters.Length);
                    var unicodeText = unicodeBuilder.ToString();
                    if (unicodeText.Length == 10
                        && unicodeText[6] == 'A'
                        && unicodeText[7] == 'é'
                        && unicodeText[8] == '\ud83d'
                        && unicodeText[9] == '\ude00')
                    {
                        observed |= 256;
                    }

                    const string escaped = "A\\u003C\\u003E\\u0026\\u0022\\u0027";
                    var rented = ArrayPool<char>.Shared.Rent(1024);
                    for (var index = 0; index < escaped.Length; index++)
                    {
                        rented[index] = escaped[index];
                    }
                    var pooledBuilder = new StringBuilder();
                    pooledBuilder.Append(rented, 0, escaped.Length);
                    ArrayPool<char>.Shared.Return(rented);
                    var pooledText = pooledBuilder.ToString();
                    if (pooledText.Length == escaped.Length && pooledText == escaped)
                    {
                        observed |= 512;
                    }

                    return observed + input - input;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StringCopyFixture",
            source,
            "StringCopyFixture.EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(895, observed);
    }

    [Fact]
    public void CompilerExecutesStringBuilderGrowthMutationAndFormatting()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StringBuilderFixture",
            """
            using System.Text;

            namespace StringBuilderFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var builder = new StringBuilder(1);
                    builder.Append('[')
                        .Append(input)
                        .Append((string?)null)
                        .Append(']', 2);
                    builder[0] = '{';
                    var text = builder.ToString();
                    var beforeClear = builder.Length * 10 + builder.Capacity;
                    builder.Clear().Append("ok");
                    var interpolated = $"{input}:{builder}";
                    return text == "{41]]" && builder.ToString() == "ok"
                        && interpolated == "41:ok"
                        ? beforeClear
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StringBuilderFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(56, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerAppendsAcrossLargeChunksWithoutFlatteningPriorText()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StringBuilderChunkGrowthFixture",
            """
            using System.Text;

            namespace StringBuilderChunkGrowthFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    const int length = 20_000;
                    var builder = new StringBuilder();
                    for (var index = 0; index < length; index++)
                    {
                        builder.Append((char)('a' + index % 26));
                    }

                    var chunkCount = 0;
                    var chunkLength = 0;
                    foreach (var chunk in builder.GetChunks())
                    {
                        chunkCount++;
                        chunkLength += chunk.Length;
                    }

                    var text = builder.ToString();
                    var runeBuilder = new StringBuilder(1).Append('A').Append('\uD83D').Append('\uDE00').Append('Ω');
                    var runeCount = 0;
                    var scalarTotal = 0;
                    foreach (var rune in runeBuilder.EnumerateRunes())
                    {
                        runeCount++;
                        scalarTotal += rune.Value;
                    }
                    return builder.Length == length && chunkLength == length && chunkCount > 1 &&
                        text.Length == length &&
                        text[0] == (char)('a' + 0 % 26) &&
                        text[7_999] == (char)('a' + 7_999 % 26) &&
                        text[8_000] == (char)('a' + 8_000 % 26) &&
                        text[19_999] == (char)('a' + 19_999 % 26) &&
                        runeCount == 3 && scalarTotal == 'A' + 0x1F600 + 'Ω'
                            ? input + 1
                            : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StringBuilderChunkGrowthFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesStringBuilderSlicesArraysCapacityAndRangeFailures()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StringBuilderSurfaceFixture",
            """
            using System;
            using System.Text;

            namespace StringBuilderSurfaceFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var builder = new StringBuilder("ab");
                    var initialCapacity = builder.Capacity;
                    var chars = new[] { '\0', '\ud800', 'Z', '\udc00' };
                    builder.Append("01234", 1, 3).Append(chars, 1, 3);
                    var ensured = builder.EnsureCapacity(40);
                    var failures = 0;
                    try { builder.Append("x", -1, 1); } catch (ArgumentOutOfRangeException) { failures++; }
                    try { _ = builder[-1]; } catch (IndexOutOfRangeException) { failures++; }
                    try { builder.Capacity = 1; } catch (ArgumentOutOfRangeException) { failures++; }
                    return initialCapacity == 16 && ensured >= 40 && builder.Length == 8
                        && builder[5] == '\ud800' && builder[7] == '\udc00'
                        ? input + failures
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StringBuilderSurfaceFixture.EntryPoint",
            "Run",
            [], Optimization: NetWasm.Compiler.ComponentModel.FinalWasmOptimization.None));

        Assert.Equal(44, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
