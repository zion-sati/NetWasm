// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A protected block left to more than one place, whose later continuations branch but return on every path, keeps its
// continuations as code after the block, each behind a test of the leave that chose it, rather than a dispatcher over
// all of them.
public sealed class TerminalContinuationCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            private static int s_cleanups;

            private static int Classify(int value)
            {
                try
                {
                    if (value > 50) return value * 2;
                    if (value < 0) goto negative;
                    if (value == 7) goto seven;
                    s_cleanups += value;
                }
                finally
                {
                    s_cleanups++;
                }
                return s_cleanups;
            negative:
                if (value < -100) return -2;
                if ((value & 1) == 0) return -4;
                return -1;
            seven:
                return value > s_cleanups ? 70 : 77;
            }

            public static int Run(int input)
            {
                var total = 0;
                s_cleanups = 0;
                foreach (var value in new[] { input, -input, input + 51, -input - 200, 7, input * 3 })
                    total = total * 31 + Classify(value);
                return total;
            }
        }
        """;

    // As desktop .NET computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, 33515129 },
        { 1, 59433303 },
        { 4, 142729168 },
        { 9, 288943416 },
        { 60, -856549050 },
        { -3, -22402368 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ContinuationsThatReturnNeedNoDispatcher(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("TerminalContinuations", Source);
        var trace = Path.Combine(assets.Directory, "trace.txt");

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3, DiagnosticTracePath: trace));

        var text = File.ReadAllText(trace);
        var start = text.IndexOf("[method EntryPoint::Classify]", StringComparison.Ordinal);
        var end = text.IndexOf("\n[method ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];
        Assert.Contains("continuation", section, StringComparison.Ordinal);
        Assert.False(section.Contains("dispatcher", StringComparison.Ordinal), section);
        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
