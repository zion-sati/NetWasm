using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskSchedulerReactorCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DeclinedSynchronousExecutionLeavesAnUnqueuedRestartableTask(bool optimized, WasmTarget target)
    {
        using var stream = typeof(TaskSchedulerReactorCompilationTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/TaskScheduler/Program.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        using var assets = TestAssets.Create();
        var source = reader.ReadToEnd();
        var assembly = optimized ? assets.CompileOptimizedSource("TaskSchedulerReactor", source)
            : assets.CompileSource("TaskSchedulerReactor", source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TaskSchedulerContracts.EntryPoint",
            "RunReactor", [new("run", "TaskSchedulerContracts.EntryPoint", "RunReactor")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 42, target, result.StaticDataEnd));
    }
}
