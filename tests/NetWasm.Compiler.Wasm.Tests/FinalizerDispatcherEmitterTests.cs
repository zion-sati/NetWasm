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
        var method = new FakeProgram().GetMethod(EntryKey);
        var declaringType = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "Test", "Type`1", false),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var finalizer = new MethodInstanceModel(
            method,
            declaringType,
            [],
            method.Signature);
        var descriptor = new FinalizerDispatchPlan(9, finalizer);

        var body = CreateEmitter(out var indices).Emit([descriptor], indices);

        Assert.True(body.AsSpan().IndexOf("\u0010\u001e"u8) >= 0);
        Assert.Contains(WasmOpcodes.TryTable, body);
    }

    private static FinalizerDispatcherEmitter CreateEmitter(
        out FunctionIndexResolver indices)
    {
        var layouts = new RecordingLayoutProvider();
        var method = new FakeProgram().GetMethod(EntryKey);
        var finalizer = new MethodInstanceModel(
            method,
            CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Assembly, "Test", "Type`1", false),
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]),
            [],
            method.Signature);
        var map = new FunctionIndexMap(
            [],
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                finalizer.CanonicalName, new(30)),
            [],
            []);
        indices = new FunctionIndexResolver(new FakeProgram(), new FakeProgram(), map);
        return new(
            layouts,
            new ExceptionPayloadBlockEmitter(layouts),
            new GeneratedFunctionWriterFactory());
    }
}
