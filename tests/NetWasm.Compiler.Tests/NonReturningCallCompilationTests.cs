using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A call to a method whose body has no ret ends its block, as an inline throw does, so the code
// only its fall-through reaches is dead: here the calls after the throw helpers are left out.
// Upstream's ThrowHelper calls rely on this to keep the values after them out of root frames.
public sealed partial class NonReturningCallCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NonReturningCalls;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                try
                {
                    return input switch
                    {
                        0 => Checked(1),
                        1 => Checked(-1),
                        2 => Value(5),
                        _ => Value(-5),
                    };
                }
                catch (InvalidOperationException)
                {
                    return 100;
                }
                catch (ArgumentException)
                {
                    return 200;
                }
            }

            private static int Checked(int value)
            {
                if (value < 0)
                {
                    Fail();
                    return AfterFail();
                }
                return 40 + value;
            }

            private static int Value(int value) => value > 0 ? value : Failed<int>() + AfterFailed();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fail() => throw new InvalidOperationException();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static T Failed<T>() => throw new ArgumentException();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AfterFail() => 1000;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AfterFailed() => 2000;
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CodeOnlyAThrowHelpersFallThroughReachesIsLeftOut(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("NonReturningCalls" + target, Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "NonReturningCalls.EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.None));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var functions = FunctionNames().Matches(printed.StandardOutput).Select(static name => name.Value).ToArray();
        Assert.Contains(functions, static name => name.Contains("::Fail", StringComparison.Ordinal));
        Assert.DoesNotContain(functions, static name => name.Contains("AfterFail", StringComparison.Ordinal));

        foreach (var (input, expected) in new[] { (0, 41), (1, 100), (2, 5), (3, 200) })
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target,
                result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\(func \$\S+")]
    private static partial Regex FunctionNames();
}
