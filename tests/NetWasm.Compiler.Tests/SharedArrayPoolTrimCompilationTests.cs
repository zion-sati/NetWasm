using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// ArrayPool's shared pool registers .NET's trim callback (Gen2GcCallback), whose trim reads the monotonic clock, only
// in a world that imports wasi:clocks/monotonic-clock. In a world without it the pool keeps what is returned to it and
// the program imports no clock: before the gate, every program that returned a pooled buffer, interpolated strings
// included, reached the clock and stopped with NW1009 outside such a world.
public sealed class SharedArrayPoolTrimCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    // Rents and returns through the shared pool across collections, so the trim callback, where registered, runs
    // after each one.
    private const string Source = """
        using System;
        using System.Buffers;

        namespace PoolTrimFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var total = 0;
                for (var round = 1; round <= 3; round++)
                {
                    var array = ArrayPool<byte>.Shared.Rent(64 + round);
                    array[0] = (byte)round;
                    total += array[0];
                    ArrayPool<byte>.Shared.Return(array);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                return total == 6 ? input + 1 : total;
            }
        }
        """;

    [Theory]
    [MemberData(nameof(Configurations))]
    public void PoolTrimsWhereTheWorldImportsTheClock(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = Compile(assets, $"PoolTrimPlatform_{target}_{optimize}", optimize);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "PoolTrimFixture.EntryPoint",
            "Run",
            [new("run", "PoolTrimFixture.EntryPoint", "Run")],
            target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Contains(ReachedTypes(result), type => type == "System.Gen2GcCallback");
        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is { InterfaceName: "wasi:clocks/monotonic-clock@0.3.2", FunctionName: "now" });
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void PoolKeepsWhatIsReturnedWithoutTheClock(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = Compile(assets, $"PoolTrimNoWorld_{target}_{optimize}", optimize);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "PoolTrimFixture.EntryPoint",
            "Run",
            [],
            target));

        Assert.DoesNotContain(ReachedTypes(result), type => type == "System.Gen2GcCallback");
        Assert.Empty(result.Program.WitImportMethods);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    private static string Compile(TestAssets assets, string name, bool optimize) =>
        optimize ? assets.CompileOptimizedSource(name, Source) : assets.CompileSource(name, Source);

    private static IEnumerable<string?> ReachedTypes(CompilationResult result) =>
        result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .Distinct();
}
