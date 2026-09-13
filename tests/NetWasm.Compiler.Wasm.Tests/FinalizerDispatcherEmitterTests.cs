using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FinalizerDispatcherEmitterTests
{
    [Fact]
    public void EmptyDispatcherReturnsNotHandledStatus()
    {
        var body = CreateEmitter(out var indices).Emit([], indices);

        Assert.Equal([0, WasmOpcodes.I32Constant, 2, WasmOpcodes.End], body);
    }

    [Fact]
    public void DispatcherCallsFinalizerThroughPlannedFunctionIndex()
    {
        var descriptor = new TypeDescriptorLayout(
            TypeKey,
            9,
            0,
            16,
            128,
            4,
            EntryKey);

        var body = CreateEmitter(out var indices).Emit([descriptor], indices);

        Assert.True(body.AsSpan().IndexOf("\u0010\u001e"u8) >= 0);
        Assert.Contains(WasmOpcodes.TryTable, body);
    }

    private static FinalizerDispatcherEmitter CreateEmitter(
        out FunctionIndexResolver indices)
    {
        var layouts = new RecordingLayoutProvider();
        var map = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey, new(30)),
            [],
            [],
            []);
        indices = new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), map);
        return new(
            layouts,
            new ExceptionPayloadBlockEmitter(layouts),
            new GeneratedFunctionWriterFactory());
    }
}
