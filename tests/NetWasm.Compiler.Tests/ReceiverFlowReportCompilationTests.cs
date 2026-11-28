// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// The receiver flow analysis's report (RECEIVER_TYPE_FLOW_DESIGN.md, step A) over a program dispatched by flow.
// Enumerable.Select calls the generic virtual Iterator<TSource>.Select<TResult> on its source: admitting every
// constructed Iterator<string> at each Select<string, R> call would instantiate K x M overrides for K sources selected
// into string and M selects from string. Each select here takes one source, so a call's targets do not grow with K.
public sealed class ReceiverFlowReportCompilationTests
{
    private static readonly string[] Elements = ["int", "long", "short", "byte", "double", "float"];

    [Fact]
    public void AProgramHasEachSelectsOwnTargets()
    {
        var (fewInProgram, fewByFlow) = SelectTargets(3);
        var (manyInProgram, manyByFlow) = SelectTargets(6);

        Assert.Equal(fewInProgram, manyInProgram);
        Assert.Equal(fewByFlow, fewInProgram);
        Assert.Equal(manyByFlow, manyInProgram);
    }

    // The most targets one Select<string, R> call has in the program and by flow, with K sources and K selects.
    private static (int InProgram, int ByFlow) SelectTargets(int count)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"ReceiverFlowSelectFixture{count}", Source(count));
        var trace = Path.Combine(assets.Directory, "receiver-flow.jsonl");
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], DiagnosticTracePath: trace,
            Optimization: FinalWasmOptimization.Oz));

        Assert.Equal(count * 2 + 3, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3,
            WasmTarget.Wasm32, result.StaticDataEnd));
        using var report = JsonDocument.Parse(File.ReadAllText(trace + ".passes/03-receiver-flow.json"));
        var selects = report.RootElement.GetProperty("genericVirtualSites").EnumerateArray()
            .Where(static site => site.GetProperty("declaration").GetString() is { } declaration &&
                declaration.Contains("Iterator`1<primitive:string>", System.StringComparison.Ordinal) &&
                declaration.Contains("]R", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(count, selects.Length);
        Assert.All(selects, static site => Assert.True(site.GetProperty("precise").GetBoolean(), site.ToString()));
        return (selects.Max(static site => site.GetProperty("targetsToday").GetInt32()),
            selects.Max(static site => site.GetProperty("targetsByFlow").GetInt32()));
    }

    private static string Source(int count)
    {
        var source = new StringBuilder("""
            using System.Collections.Generic;
            using System.Linq;

            """);
        for (var index = 0; index < count; index++)
        {
            source.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"public sealed class R{index} {{ public R{index}(string s) {{ S = s; }} public string S {{ get; }} }}");
        }
        source.AppendLine("public static class EntryPoint {");
        for (var index = 0; index < count; index++)
        {
            source.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"    private static IEnumerable<string> S{index}(int n) => new List<{Elements[index]}> {{ ({Elements[index]})n, 2 }}.Select(x => x.ToString());");
        }
        source.AppendLine("    public static int Run(int input) { var total = input;");
        for (var index = 0; index < count; index++)
        {
            source.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"        total += S{index}(input).Select(s => new R{index}(s)).Count();");
        }
        source.AppendLine("        return total; } }");
        return source.ToString();
    }
}
