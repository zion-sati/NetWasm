// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StringBuilderIndexerContractCompilationTests
{
    // The indexer as desktop .NET's: get_Chars and set_Chars. 0.16 rebuilds every library, so libraries built against
    // 0.15's get_Item and set_Item forwarders need a rebuild, as for Array.Copy<T>.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TheIndexerPreservesIndexingContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "IndexerFixture", Source, "IndexerFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Text;
        namespace IndexerFixture;
        public static class EntryPoint
        {
            private static char Read(StringBuilder value, int index) => value[index];
            private static void Write(StringBuilder value, int index, char item) { value[index] = item; }
            public static int Run(int input)
            {
                var builder = new StringBuilder(2);
                builder.Append("ab").Append("cdef").Append('x', 9000);
                if (Read(builder, 0) != 'a' || Read(builder, 5) != 'f' || Read(builder, builder.Length - 1) != 'x') return 1;
                Write(builder, 1, 'B'); Write(builder, 5000, 'Y');
                if (Read(builder, 1) != 'B' || Read(builder, 5000) != 'Y') return 2;
                foreach (var index in new[] { -1, int.MinValue, builder.Length, int.MaxValue })
                {
                    try { Read(builder, index); return 3; } catch (IndexOutOfRangeException) { }
                    try { Write(builder, index, 'z'); return 4; }
                    catch (ArgumentOutOfRangeException e) { if (e.ParamName != "index") return 5; }
                }
                builder.Clear();
                try { Read(builder, 0); return 6; } catch (IndexOutOfRangeException) { }
                try { Write(builder, 0, 'z'); return 7; } catch (ArgumentOutOfRangeException) { }
                builder.Append("reuse");
                if (Read(builder, 0) != 'r' || Read(builder, 4) != 'e') return 8;
                return input + 1;
            }
        }
        """;
}
