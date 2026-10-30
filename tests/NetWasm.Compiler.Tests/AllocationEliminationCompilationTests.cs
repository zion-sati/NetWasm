using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class AllocationEliminationCompilationTests
{
    private const string Source = """
        public interface IShape
        {
            int Sides { get; }
        }
        public sealed class Square : IShape
        {
            public int Sides => 4;
        }
        public sealed class Point
        {
            private readonly int _x;
            private readonly int _y;
            public Point(int x, int y) { _x = x; _y = y; }
        }
        public sealed class Counted
        {
            public static int Made;
            public Counted() => Made++;
        }
        public sealed class Tracked
        {
            ~Tracked() { }
        }
        public static class EntryPoint
        {
            private static int _reads;
            private static int Next() => ++_reads;
            private static int Sides<T>() where T : IShape, new() => new T().Sides;
            public static int Run(int input)
            {
                var total = 0;
                for (var index = 0; index < input; index++)
                {
                    total += Sides<Square>();
                    // Unused objects; the arguments still run.
                    var point = new Point(Next(), index);
                    var counted = new Counted();
                    var tracked = new Tracked();
                }
                return total * 1000 + _reads * 10 + Counted.Made;
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void UnusedAllocationsDisappearWhileObservableOnesStay(bool optimize, WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("AllocationElimination", Source) : assets.CompileSource("AllocationElimination", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));
        var run = result.Program.Methods.Values.Single(body => body.Method.Definition.Name == "Run");
        var allocated = run.Body.Instructions
            .Where(static instruction => instruction.Operation == CilOperation.NewObject)
            .Select(static instruction => ((CilOperand.MethodInstance)instruction.Operand).Value.DeclaringType.FullName)
            .ToHashSet();

        // The constructor with an effect and the finalizable object always stay.
        Assert.Contains("Counted", allocated);
        Assert.Contains("Tracked", allocated);
        Assert.Equal(preset == FinalWasmOptimization.None, allocated.Contains("Point"));
        // At O3, and at Oz because the call is inside the loop, Sides<Square> inlines, so its Square goes as well.
        Assert.Equal(preset != FinalWasmOptimization.None, !allocated.Contains("Square") &&
            !result.Program.ConstructedMethods.Values.Any(static body => body.Method.Definition.Name == "Sides"));
        foreach (var input in new[] { 0, 3 })
        {
            Assert.Equal(input * 4 * 1000 + input * 10 + input,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }
}
