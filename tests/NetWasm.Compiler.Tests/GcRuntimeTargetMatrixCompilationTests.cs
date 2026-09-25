using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GcRuntimeTargetMatrixCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerValidatesGcRuntimeContractsAcrossCilAndAddressWidths(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "GcRuntimeTargetMatrixFixture",
            Source,
            "GcRuntimeTargetMatrixFixture.EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(7, observed);
    }

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        namespace GcRuntimeTargetMatrixFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object target = new object();
                var handle = GCHandle.Alloc(target);
                var strongHandleWorks = object.ReferenceEquals(handle.Target, target);
                handle.Target = null;
                handle.Free();

                var weak = new WeakReference<object>(target);
                var weakHandleWorks = weak.TryGetTarget(out var weakTarget)
                    && object.ReferenceEquals(weakTarget, target);
                var identityHashWorks = RuntimeHelpers.GetHashCode(target) != 0;
                var values = GC.AllocateArray<int>(2);
                values[1] = 7;
                GC.KeepAlive(target);
                return strongHandleWorks && weakHandleWorks && identityHashWorks ? values[1] + input : -1;
            }
        }
        """;
}
