using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class StaticInitializationHoistingCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class Table
        {
            public static int Count = 100;
            public static readonly int[] Values = [1, 2, 3];
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var sum = 0;
                for (var i = 0; i < Table.Count; i++) sum += Table.Values[i % Table.Values.Length];
                return sum + Maybe(input) + Guarded(input);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Maybe(int input) => input > 0 ? Table.Count : 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Guarded(int input)
            {
                try { return Table.Count; }
                catch (TypeInitializationException) { return -1; }
            }
        }
        """;

    // A loop over a BeforeFieldInit type's statics checked the type on every
    // access, and kept references in a root frame across the possible
    // initializer call. A method that accesses the type on every path to a
    // return checks it once, at entry, and its other accesses check nothing.
    [Fact]
    public void O3ChecksABeforeFieldInitTypeOnceAtTheEntryOfAMethodThatAlwaysAccessesIt()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("StaticInitializationHoisting", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Optimization: FinalWasmOptimization.O3));
        CilMethodBody Body(string name) => compilation.Program.Methods.Values
            .Single(method => method.Method.Definition.Name == name).Body;

        var run = Body("Run").Instructions;
        Assert.Equal([CilOperation.LoadStaticFieldAddress, CilOperation.Pop], run.Take(2).Select(i => i.Operation));
        // One entry check covers the type, so the loop's accesses are left as they were.
        Assert.Single(run, instruction => instruction.Operation == CilOperation.Pop &&
            run[run.IndexOf(instruction) - 1].Operation == CilOperation.LoadStaticFieldAddress);
        // An access on only some paths keeps its check there, and a method with
        // exception regions keeps its checks where they can be handled.
        Assert.NotEqual(CilOperation.LoadStaticFieldAddress, Body("Maybe").Instructions[0].Operation);
        Assert.NotEqual(CilOperation.LoadStaticFieldAddress, Body("Guarded").Instructions[0].Operation);
        Assert.Equal(399, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 1));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, true, 1, 399)]
    [InlineData(WasmTarget.Wasm32, true, 0, 299)]
    [InlineData(WasmTarget.Wasm64, true, 1, 399)]
    [InlineData(WasmTarget.Wasm32, false, 1, 399)]
    public void HoistedChecksReadTheInitializedValues(WasmTarget target, bool optimize, int input, int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"StaticInitializationHoisting_{target}_{optimize}",
            Source,
            "EntryPoint",
            Optimize: optimize,
            target,
            input,
            [])));
    }
}
