using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BufferAndMemoryCompatibilityCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerValidatesBufferAndMemoryContractsForEveryProfile(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "BufferAndMemoryCompatibilityFixture",
            Source,
            "BufferAndMemoryCompatibilityFixture.EntryPoint",
            optimize,
            target,
            42,
            []));

        Assert.Equal(0, observed);
    }

    private const string Source = """
        using System;
        using System.Buffers;

        namespace BufferAndMemoryCompatibilityFixture;

        public enum Sample
        {
            FortyTwo = 42,
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                try
                {
                    if (Sample.FortyTwo.ToString("D") != "42") return 10;
                    var formatDestination = new char[4];
                    if (!((ISpanFormattable)Sample.FortyTwo).TryFormat(
                        formatDestination,
                        out var charsWritten,
                        "D",
                        null) ||
                        charsWritten != 2 ||
                        formatDestination[0] != '4' ||
                        formatDestination[1] != '2')
                    {
                        return 1;
                    }
                }
                catch
                {
                    return 11;
                }

                try
                {
                    var searchValues = SearchValues.Create(
                        new string[] { "--", "|" },
                        StringComparison.Ordinal);
                    if (!searchValues.Contains("--") || searchValues.Contains("-")) return 2;

                    ReadOnlySpan<char> source = " a--b| c ";
                    if (source.IndexOfAny(searchValues) != 2) return 3;
                    var ranges = new Range[4];
                    var count = source.SplitAny(
                        ranges,
                        new string[] { "--", "|" },
                        StringSplitOptions.TrimEntries);
                    if (count != 3 ||
                        !source[ranges[0]].SequenceEqual("a") ||
                        !source[ranges[1]].SequenceEqual("b") ||
                        !source[ranges[2]].SequenceEqual("c"))
                    {
                        return 4;
                    }
                }
                catch
                {
                    return 12;
                }

                try
                {
                    Memory<int> memory = new[] { input, 7 };
                    ReadOnlyMemory<int> readOnly = memory;
                    if (memory.IsEmpty || memory.Length != 2 ||
                        readOnly.IsEmpty || readOnly.Length != 2 ||
                        readOnly.Span[0] != input || Memory<int>.Empty.Length != 0)
                    {
                        return 5;
                    }
                }
                catch
                {
                    return 13;
                }

                return input - 42;
            }
        }
        """;
}
