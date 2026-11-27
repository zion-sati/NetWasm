// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// The receiver flow audit (RECEIVER_TYPE_FLOW_DESIGN.md, step B): every dispatched call the analysis follows
// precisely checks its receiver against the analysis's set and fails fast outside it, dispatch unchanged. Programs
// whose receivers reach their calls through each source kind, and LINQ's iterator chains, run as without it.
public sealed class ReceiverFlowAuditCompilationTests
{
    private const string Linq = """
        using System.Collections.Generic;
        using System.Linq;

        public sealed class Named { public Named(string name) { Name = name; } public string Name { get; } }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var numbers = Enumerable.Range(0, input + 5).Where(x => x % 2 == 0).Select(x => x * 3).ToList();
                var names = numbers.Select(x => x.ToString()).Select(s => new Named(s)).Where(n => n.Name.Length < 3);
                var grouped = names.GroupBy(n => n.Name.Length).OrderBy(g => g.Key).Select(g => g.Count()).ToArray();
                var set = new HashSet<long>(numbers.Select(x => (long)x));
                return grouped.Sum() * 100 + set.Count + new[] { 1.5, 2.5 }.Select(d => (int)d).Sum();
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AuditedSourceKindsRunAsWithoutTheAudit(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowSourceKindFixture", ReceiverFlowSourceKindCompilationTests.Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        Assert.Equal(22, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, target,
            result.StaticDataEnd));
    }

    // Type objects and handles the runtime makes (GetType, typeof, Enum's names) and a field set through a cached
    // delegate's branch and Interlocked.CompareExchange, whose address crosses blocks: the audit failed at each before
    // the analysis treated compiler-only instructions, handle fields and addresses across blocks as unknown.
    private const string RuntimeMade = """
        using System;
        using System.Threading;

        public enum Color { Red, Green, Blue }
        public enum Wide : long { A = 1, B = 1L << 40 }
        public abstract class Greeter { public abstract int Greet(); }
        public sealed class Hello : Greeter
        {
            private readonly Func<int, int> _twice;
            public Hello(Func<int, int> twice) { _twice = twice; }
            public override int Greet() => _twice(5);
        }

        public static class EntryPoint
        {
            private static Greeter? s_greeter;

            private static Greeter Greeter()
            {
                if (s_greeter == null) Interlocked.CompareExchange(ref s_greeter, new Hello(static x => x * 2), null);
                return s_greeter!;
            }

            public static int Run(int input)
            {
                var total = 0;
                total += Enum.GetName(typeof(Color), Color.Green)!.Length;
                total += Enum.GetName(typeof(Color), input - 2)!.Length;
                total += Enum.GetName(typeof(Wide), Wide.B)!.Length;
                total += Enum.GetName(Color.Blue)!.Length;
                total += ((Color)input).ToString().Length;
                total += typeof(Hello).ToString().Length > 0 ? 100 : 0;
                total += new Hello(x => x).GetType() == typeof(Hello) ? 1000 : 0;
                return total + Greeter().Greet() * 10000;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AuditedRuntimeMadeObjectsRunAsWithoutTheAudit(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAuditRuntimeMadeFixture", RuntimeMade);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        // Green 5, Blue 4 (input - 2 = 1 is Green: 5), B 1, Blue 4, Blue 4 (input 3 is not a name: "3", 1).
        Assert.Equal(5 + 5 + 1 + 4 + 1 + 100 + 1000 + 10 * 10000, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 3, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AuditedLinqRunsAsWithoutTheAudit(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowAuditLinqFixture", Linq);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        // input 3: 0, 6, 12, 18 as strings, all shorter than 3: four names in two lengths; four longs; 1 + 2.
        Assert.Equal(4 * 100 + 4 + 3, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, target,
            result.StaticDataEnd));
    }
}
