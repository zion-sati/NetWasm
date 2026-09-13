using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StackLocalResolverTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedReference, 1)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ValueType, 1)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, 4)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedReference, 13)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ValueType, 16)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.NativeInt, 4)]
    public void ResolvesStackStorageForEachTargetWidth(
        WasmTarget target,
        CliValueKind type,
        int expected)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var resolver = ThroughContract(new StackLocalResolver(layouts));

        Assert.Equal(expected, resolver.Resolve(
            CreateMethodEmissionContext(),
            1,
            type));
    }

    private static IStackLocalResolver ThroughContract(
        StackLocalResolver resolver) => new[]
        {
            resolver,
        }.Cast<IStackLocalResolver>().Single();
}
