// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Structuring an else-if chain whose arms rejoin at the code after it recursed once per arm and nested the output
// as deep: 3,000 arms crashed the compiler on a thread pool thread's stack (run-time Type attribute queries build
// such chains, one arm per candidate type), and 20,000 took minutes and tens of gigabytes, overflowed Binaryen's
// stack and aborted V8. A chain is now walked without recursion and emitted flat, so 20,000 arms compile and run as
// on desktop .NET: Debug IL at O0 and Release IL optimized, the pairs the SDK builds.
public sealed class DeepNestingCompilationTests
{
    private const int Arms = 20000;
    private static readonly int[] DebugArms = [200, 400];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void AnElseIfChainOfThousandsOfArmsCompilesAndRuns(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            "DeepNestingFixture",
            Source(Arms),
            "DeepNestingFixture.EntryPoint",
            optimize,
            target,
            Arms,
            [])
        {
            Optimization = optimize ? TestOptimization.Default : FinalWasmOptimization.O0,
        });

        Assert.Equal(Expected(Arms), observed);
    }

    // Debug IL stores each condition in a bool of its own. Without wasm-opt, which merges locals at every other
    // preset, each IL local was one of the function's locals, and V8 keeps the locals' state at every block: 20,000
    // arms took it gigabytes and aborted it. At O0 the conditions' locals share one, so the function's locals do not
    // grow with its arms.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ADebugChainsLocalsDoNotGrowWithItsArmsAtO0(WasmTarget target)
    {
        using var assets = TestAssets.Create();

        var locals = DebugArms.Select(arms =>
        {
            var assembly = assets.CompileSource($"DeepNestingDebug{arms}_{target}", Source(arms));
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly, [assets.CoreLib], "DeepNestingFixture.EntryPoint", "Run", [],
                Target: target, Optimization: FinalWasmOptimization.O0));
            Assert.Equal(Expected(arms),
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, arms, target, result.StaticDataEnd));
            return Locals(assets, result);
        }).ToArray();

        Assert.All(locals, count => Assert.Equal(locals[0], count));
        Assert.InRange(locals[0], 1, 100);
    }

    // Sums Classify over every arm and a few values past them, as desktop .NET computes it.
    private static int Expected(int arms)
    {
        var sum = 0;
        for (var value = 0; value < arms + 5; value++)
        {
            sum += Classify(value);
        }
        return sum;

        int Classify(int value) => (value < arms ? value * 7 % 101 : -1) * 2 + 1;
    }

    // The locals Classify's function declares.
    private static int Locals(TestAssets assets, CompilationResult result)
    {
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var function = Regex.Match(printed.StandardOutput,
            @"\(func \$[^\s]*::Classify\b.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline);
        Assert.True(function.Success, "Classify was not emitted.");
        return Regex.Matches(function.Value, @"^\s*\(local ([^)]*)\)", RegexOptions.Multiline)
            .Sum(declaration => declaration.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static string Source(int arms)
    {
        var source = new StringBuilder("""
            namespace DeepNestingFixture;

            public static class EntryPoint
            {
                public static int Run(int arms)
                {
                    var sum = 0;
                    for (var value = 0; value < arms + 5; value++)
                    {
                        sum += Classify(value);
                    }
                    return sum;
                }

                private static int Classify(int value)
                {
                    int result;
                    if (value == 0) result = 0;

            """);
        for (var arm = 1; arm < arms; arm++)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"        else if (value == {arm}) result = {arm * 7 % 101};");
        }
        source.Append("""
                    else result = -1;
                    return result * 2 + 1;
                }
            }
            """);
        return source.ToString();
    }
}
