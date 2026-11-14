using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Arrays whose elements are pointers (zion-sati/NetWasm#235) store, load, box and type-test as on desktop .NET.
public sealed class PointerArrayCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ArraysOfPointersStoreAndLoadTheirElements(WasmTarget target, bool optimized)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        var scenario = new CompilationScenario("PointerArrays", Source, "PointerArrays.EntryPoint", false, target, 41, [])
        {
            AllowUnsafe = true,
        };
        // Unoptimized, as an SDK Debug build is: type names and stack traces on, which 0.13.0 failed (NW1003).
        Assert.Equal(42, executor.Execute(optimized ? scenario : scenario with
        {
            Optimization = FinalWasmOptimization.None,
            EmitTypeNames = true,
            EmitStackTrace = true,
        }));
    }

    private const string Source = """
        namespace PointerArrays;
        public static unsafe class EntryPoint
        {
            public static int Run(int input)
            {
                void*[] pointers = new void*[2];
                int value = input;
                pointers[0] = &value;
                int*[] integers = new int*[1];
                integers[0] = (int*)pointers[0];
                *integers[0] += 1;
                if (pointers[1] != null || integers.Length != 1) return 1;
                object boxed = pointers;
                if (boxed is not void*[] again || again.Length != 2) return 2;
                return *(int*)pointers[0];
            }
        }
        """;
}
