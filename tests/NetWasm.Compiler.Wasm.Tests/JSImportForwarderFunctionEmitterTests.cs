using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JSImportForwarderFunctionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 2, 3, 2)]
    [InlineData(WasmTarget.Wasm64, 4, 3, 2)]
    public void CopiesParametersIntoStackSlotsInvokesTheImportAndReturnsItsResult(
        WasmTarget target,
        int referenceSlot,
        int scalarSlot,
        int resultSlot)
    {
        var program = new FakeProgram();
        var import = Import(program, CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.I4);
        var invocations = new RecordingInvocationEmitter();
        var moduleTarget = CreateInstructionModuleTarget(program);
        var functionIndices = CreateFunctionIndexResolver(program);

        var body = Emitter(target, invocations).Emit(import, moduleTarget, functionIndices);

        var invocation = Assert.Single(invocations.Invocations);
        Assert.Same(import, invocation.Method);
        Assert.Equal([CliValueKind.ManagedReference, CliValueKind.I4], invocation.Stack);
        Assert.Equal(0, invocation.ArgumentBase);
        Assert.Equal(2, invocation.Consumed);
        Assert.Same(moduleTarget, invocation.Target);
        Assert.Same(functionIndices, invocations.FunctionIndices);
        var context = invocation.Context;
        Assert.Equal(2, context.StackLocals.SlotCount);
        Assert.Equal(context.StackLocals.End, context.ObjectTemporary);
        Assert.True(context.InteropDescriptor > context.ObjectTemporary);
        Assert.Equal(context.InteropDescriptor + 1, context.InteropResult);
        Assert.Equal(context.InteropResult + 1, context.InteropHandle);
        Assert.Equal(moduleTarget.RuntimeImportSelection, context.RuntimeImportSelection);
        Assert.Equal(
            [
                WasmOpcodes.LocalGet, 0, WasmOpcodes.LocalSet, (byte)referenceSlot,
                WasmOpcodes.LocalGet, 1, WasmOpcodes.LocalSet, (byte)scalarSlot,
                WasmOpcodes.Unreachable,
                WasmOpcodes.LocalGet, (byte)resultSlot,
                WasmOpcodes.End,
            ],
            body[^12..]);
    }

    [Fact]
    public void VoidImportsReturnNothingAfterTheInvocation()
    {
        var program = new FakeProgram();
        var import = Import(program, CliValueKind.Void, CliValueKind.I4);

        var body = Emitter(WasmTarget.Wasm32, new RecordingInvocationEmitter()).Emit(
            import,
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        Assert.Equal(
            [WasmOpcodes.LocalGet, 0, WasmOpcodes.LocalSet, 1, WasmOpcodes.Unreachable, WasmOpcodes.End],
            body[^6..]);
    }

    [Fact]
    public void ParameterlessImportsKeepOneStackSlotForTheResult()
    {
        var program = new FakeProgram();
        var invocations = new RecordingInvocationEmitter();

        var body = Emitter(WasmTarget.Wasm32, invocations).Emit(
            Import(program, CliValueKind.I4),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        var invocation = Assert.Single(invocations.Invocations);
        Assert.Empty(invocation.Stack);
        Assert.Equal(0, invocation.Consumed);
        Assert.Equal(1, invocation.Context.StackLocals.SlotCount);
        Assert.Equal([WasmOpcodes.Unreachable, WasmOpcodes.LocalGet, 0, WasmOpcodes.End], body[^4..]);
    }

    private static IJSImportForwarderFunctionEmitter Emitter(
        WasmTarget target,
        IJSImportInvocationEmitter invocations) =>
        Assert.IsAssignableFrom<IJSImportForwarderFunctionEmitter>(new JSImportForwarderFunctionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)),
            invocations,
            new GeneratedFunctionWriterFactory()));

    private static MethodInstanceModel Import(
        FakeProgram program,
        CliValueKind result,
        params CliValueKind[] parameters)
    {
        var definition = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(result, parameters),
            JSImport = new("resolve", "tests"),
        };
        return new(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Host", isValueType: false),
            [],
            definition.Signature);
    }

    // Writes one marker instruction so tests can locate the invocation in the body.
    private sealed class RecordingInvocationEmitter : IJSImportInvocationEmitter
    {
        public List<JSImportInvocation> Invocations { get; } = [];
        public IFunctionIndexResolver? FunctionIndices { get; private set; }

        public void Emit(
            JSImportInvocation invocation,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Invocations.Add(invocation with { Stack = [.. invocation.Stack] });
            FunctionIndices = functionIndices;
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        }
    }
}
