// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// The generic cycle cutoff gives each method definition a breadth of grown instances that run their code, and counts on
// discovery running level by level so the shallow instances a program reaches take it. Calls that dispatch by the
// receiver flow hold their candidates until the flow admits them; released by name rather than in the order the closure
// filed them, deep instantiations took that breadth, ran their code and multiplied PLINQ's binary operators' partition
// keys: about 387,000 instances of one method here, and an out-of-memory compile, where filing order keeps 6,889. Every
// binary operator below works on int, so the analysis cannot tell their recipients apart, as in any program using them.
public sealed class GatedDispatchGenericGrowthCompilationTests
{
    internal const string Source = """
        using System;
        using System.Linq;
        namespace GatedDispatchGrowth;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var numbers = Enumerable.Range(0, 100).ToArray();
                var concat = numbers.AsParallel().AsOrdered().Concat(numbers.AsParallel().AsOrdered()).Take(3).ToArray().Length;
                var join = numbers.AsParallel().Join(numbers.AsParallel(), x => x, y => y, (x, y) => x + y).Sum();
                var zip = numbers.AsParallel().AsOrdered().Zip(numbers.AsParallel().AsOrdered(), (x, y) => x + y).Take(2)
                    .ToArray().Length;
                var sorted = numbers.AsParallel().OrderBy(x => -x).ThenBy(x => x).Take(1).ToArray()[0];
                return concat + zip + (join == 9900 ? 10 : 0) + (sorted == 99 ? 100 : 0) + input;
            }
        }
        """;

    // No method definition has more instances than this; the largest has 6,889.
    private const int MostInstancesOfOneDefinition = 10_000;

    [Fact]
    public void BinaryOperatorsOverOneTypeKeepTheirGenericGrowthWithinTheCutoff()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("GatedDispatchGrowth", Source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "GatedDispatchGrowth.EntryPoint",
            "Run", [new("run", "GatedDispatchGrowth.EntryPoint", "Run")], Optimization: FinalWasmOptimization.Oz,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        var largest = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(static body => body.Method)
            .DistinctBy(static method => method.CanonicalName)
            .GroupBy(static method => method.Definition.Key)
            .MaxBy(static group => group.Count())!;
        Assert.True(largest.Count() <= MostInstancesOfOneDefinition,
            $"{largest.First().Definition.Name} of {largest.First().DeclaringType.CanonicalName} has {largest.Count()} instances");
        Assert.Equal(3 + 2 + 10 + 100 + 4, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 4,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
