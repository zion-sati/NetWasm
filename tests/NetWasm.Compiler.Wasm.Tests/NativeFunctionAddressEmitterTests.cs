using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NativeFunctionAddressEmitterTests
{
    [Fact]
    public void CallsTheExactPlannedGetter()
    {
        var callback = CallbackPlan(73);
        var code = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<INativeFunctionAddressEmitter>(
            new NativeFunctionAddressEmitter());

        emitter.Emit(callback, code);

        var instruction = Assert.Single(code.ToInstructions());
        Assert.Equal(WasmOpcodes.Call, instruction.Opcode);
        Assert.Equal(73U, instruction.Operand.UnsignedValue);
    }

    [Fact]
    public void RejectsMissingInputsBeforeWriting()
    {
        var emitter = Assert.IsAssignableFrom<INativeFunctionAddressEmitter>(
            new NativeFunctionAddressEmitter());
        var code = new RecordingInstructionWriter();

        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, code));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(CallbackPlan(1), null!));
        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            CallbackPlan(null),
            code));
        Assert.Empty(code.ToInstructions());
    }

    private static NativeCallbackMethodPlan CallbackPlan(int? getterIndex)
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(EntryKey);
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Type", false),
            [],
            definition.Signature);
        var abi = NativeAbiTestSupport.ScalarSignaturePlanner().Plan(
            method.Signature,
            NativeAbiSignatureKind.Callback,
            method.CanonicalName);
        return new(
            method,
            abi,
            "native",
            "native",
            "thunk",
            getterIndex is null ? null : "getter",
            getterIndex is { } value ? new(value) : null);
    }
}
