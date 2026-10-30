using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// RuntimeHelpers.IsReferenceOrContainsReferences<T>() folds before reachability, so a callee that
// only the reference case calls is left out of programs whose instantiations never take it, as
// upstream's Span<T>(void*, int) relies on for ThrowArgument_TypeContainsReferences.
public sealed partial class ContainsReferencesFoldingCompilationTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        namespace ContainsReferencesFolding;

        public struct Numbers { public int Value; public long Other; }
        public struct Nested { public Numbers Inner; public nint Pointer; }
        public struct Holder { public int Value; public string? Text; }
        public struct Wrapper<T> { public T Value; }
        public enum Kind { First, Second }

        public static class EntryPoint
        {
            public static int Run(int input) =>
                Absent<int>() + Absent<Numbers>() + Absent<Nested>() + Absent<KeyValuePair<int, long>>() +
                Absent<Kind>() + Absent<Wrapper<double>>() +
                Present<Holder>() + Present<object>() + Present<Wrapper<string>>() +
                Present<System.Span<int>>() + input;

            private static int Absent<T>() where T : allows ref struct =>
                RuntimeHelpers.IsReferenceOrContainsReferences<T>() ? OnlyForReferences() : 1;

            private static int Present<T>() where T : allows ref struct =>
                RuntimeHelpers.IsReferenceOrContainsReferences<T>() ? NeededForReferences() : 100;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OnlyForReferences() => 1000;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NeededForReferences() => 10;
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ClosedInstantiationsDropTheReferenceOnlyCallee(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ContainsReferencesFolding" + target, Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "ContainsReferencesFolding.EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.None));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var functions = FunctionNames().Matches(printed.StandardOutput).Select(static name => name.Value).ToArray();
        Assert.Contains(functions, static name => name.Contains("NeededForReferences", StringComparison.Ordinal));
        Assert.DoesNotContain(functions, static name => name.Contains("OnlyForReferences", StringComparison.Ordinal));

        // Six instantiations without references and four with them, as on desktop.
        Assert.Equal(46, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    [GeneratedRegex(@"\(func \$\S+")]
    private static partial Regex FunctionNames();
}
