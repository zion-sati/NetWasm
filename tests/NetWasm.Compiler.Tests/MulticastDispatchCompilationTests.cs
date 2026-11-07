using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A delegate type's multicast function dispatches each entry itself at O1 to O3 and
// hands each entry to the type's invoke function at the size presets, unless
// NetWasmManagedMulticastInlineDispatch says otherwise; the invoke function calls the
// multicast function for a delegate with a list either way. A multicast and a
// single-cast Action give C#'s result in every mode.
public sealed class MulticastDispatchCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hits = input;
                Action multicast = () => hits = hits * 3 + 1;
                multicast += () => hits += 10;
                multicast();
                Action single = () => hits -= 2;
                single();
                return hits;
            }
        }
        """;

    // hits * 3 + 1, then + 10, then - 2.
    private static readonly (int Input, int Expected)[] Cases = [(0, 9), (1, 12), (5, 24), (-3, 0)];

    private const string Invoke = "delegate.invoke<[NetWasm.CoreLib]System.Action>";
    private const string Multicast = "delegate.multicast<[NetWasm.CoreLib]System.Action>";

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, ManagedMulticastInlineDispatch.Auto, true)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3, ManagedMulticastInlineDispatch.Disabled, false)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz, ManagedMulticastInlineDispatch.Auto, false)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz, ManagedMulticastInlineDispatch.Enabled, true)]
    public void DispatchesMulticastEntriesInlineOnlyWhereTheFlagSays(WasmTarget target,
        FinalWasmOptimization optimization, ManagedMulticastInlineDispatch dispatch, bool inline)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"MulticastDispatch_{target}_{optimization}_{dispatch}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: optimization,
            OptimizationOverrides: new WasmOptimizationOverrides(MulticastInlineDispatch: dispatch)));

        var printed = Print(assets, result);
        Assert.Contains($"call $\"{Multicast}\"", Function(printed, Invoke), StringComparison.Ordinal);
        Assert.Equal(!inline, Function(printed, Multicast).Contains($"call $\"{Invoke}\"", StringComparison.Ordinal));
        foreach (var (input, expected) in Cases)
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static string Print(TestAssets assets, CompilationResult result)
    {
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return printed.StandardOutput;
    }

    private static string Function(string printed, string name)
    {
        var function = Regex.Match(printed,
            $@"\(func \$""{Regex.Escape(name)}"".*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline);
        Assert.True(function.Success, $"{name} is not in the module");
        return function.Value;
    }
}
