using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class WholeProgramReductionOptionsCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void IndependentOverridesPreserveResultsAndWarmCacheIsolation(bool optimize, WasmTarget target)
    {
        const string source = """
            public readonly struct Value
            {
                public readonly int Number;
                public Value(int number) { Number = number; }
            }
            public static class Data
            {
                private static readonly Value[] Unused = [new(7)];
                public static readonly int Trigger = 2;
            }
            public static class EntryPoint
            {
                private static int Slow(int value) => value + 1000;
                private static bool IsDefault(string? format) => format is null;
                private static int Choose(int value, string? format) => IsDefault(format) ? value : Slow(value);
                public static int Run(int input) => Choose(input, null) + Data.Trigger;
            }
            """;
        using var assets = TestAssets.Create();
        // Keep the affected bodies in a dependency, as a reused library would be.
        var library = optimize
            ? assets.CompileOptimizedSource("WholeProgramControls.Library", source)
            : assets.CompileSource("WholeProgramControls.Library", source);
        const string application = """
            public static class Application
            {
                public static int Run(int input) => EntryPoint.Run(input);
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("WholeProgramControls", application, library)
            : assets.CompileSource("WholeProgramControls", application, library);
        var options = new CompilerOptions(assembly, [assets.CoreLib, library], "Application", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.Oz,
            IntermediateOutputPath: Path.Combine(assets.Directory, "obj"));
        foreach (var constants in new[] { false, true })
            foreach (var population in new[] { false, true })
            {
                var selected = options with
                {
                    OptimizationOverrides = new(
                        ConstantArgumentPropagation: constants ? ManagedConstantArgumentPropagation.Enabled : ManagedConstantArgumentPropagation.Disabled,
                        StaticArrayPopulationElimination: population ? ManagedStaticArrayPopulationElimination.Enabled : ManagedStaticArrayPopulationElimination.Disabled),
                };
                var first = NetWasmCompiler.Compile(selected);
                var warm = NetWasmCompiler.Compile(selected);
                Assert.Equal(first.ApplicationModule, warm.ApplicationModule);
                Assert.Equal(!constants, warm.Program.Methods.Values.Any(method =>
                    method.Method.DeclaringType.FullName == "EntryPoint" && method.Method.Definition.Name == "Slow"));
                var initializer = Assert.Single(warm.Program.Methods.Values, method =>
                    method.Method.DeclaringType.FullName == "Data" && method.Method.Definition.Name == ".cctor");
                Assert.Equal(population ? 0 : 1, initializer.Body.Instructions.Count(instruction => instruction.Operation == CilOperation.NewObject));
                Assert.Single(initializer.Body.Instructions, instruction => instruction.Operation == CilOperation.NewArray);
                Assert.Equal(42, ExecuteWithStandardWasiNode(warm.ApplicationModule,
                    assets.Directory, 40, target, warm.StaticDataEnd));
            }
    }

}
