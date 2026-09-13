using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayLengthIntrinsicEmitterTests
{
    [Fact]
    public void EmitsLengthLoadAndNullFailurePath()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new ArrayLengthIntrinsicEmitter(
            new ArrayLengthEmitter(
                layouts,
                new ArrayReceiverValidator(
                    new ImplicitExceptionEmitter(layouts, layouts, 7),
                    CreateAddressInstructions(layouts))));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayLength,
            [CliValueKind.ManagedReference]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        var code = GetCodeBytes(request.Instruction);
        Assert.Contains(WasmOpcodes.If, code);
        Assert.Contains(WasmOpcodes.I32Load, code);
        Assert.Contains(WasmOpcodes.I32EqualZero, code);
    }

    [Fact]
    public void LengthCapabilityRequestsNullCheckAndLoadsArrayHeader()
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new RecordingAddressInstructionEmitter();
        var exceptions = new RecordingImplicitExceptionEmitter();
        var emitter = new IArrayLengthEmitter[]
        {
            new ArrayLengthEmitter(
                layouts,
                new ArrayReceiverValidator(exceptions, addresses)),
        }.Single();
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayLength,
            [CliValueKind.ManagedReference]);

        emitter.EmitLength(request, GetCodeWriter(request.Instruction));

        Assert.Equal([AddressOperation.EqualZero], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.NullReference], exceptions.Kinds);
        var code = GetCodeBytes(request.Instruction);
        Assert.Contains(WasmOpcodes.I32Load, code);
        Assert.Contains((byte)layouts.ArrayLengthOffset, code);
    }

    [Fact]
    public void IntrinsicAdapterDelegatesToLengthCapability()
    {
        var capability = new RecordingArrayLengthEmitter();
        var emitter = new IRuntimeIntrinsicEmitter[]
        {
            new ArrayLengthIntrinsicEmitter(capability),
        }.Single();
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayLength,
            [CliValueKind.ManagedReference]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        Assert.Same(request, capability.Request);
        Assert.Same(GetCodeWriter(request.Instruction), capability.Writer);
    }

    private sealed class RecordingArrayLengthEmitter : IArrayLengthEmitter
    {
        public RuntimeIntrinsicEmissionRequest? Request { get; private set; }
        public IWasmInstructionWriter? Writer { get; private set; }

        public void EmitLength(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code)
        {
            Request = request;
            Writer = code;
        }
    }

    private sealed class RecordingAddressInstructionEmitter : IAddressInstructionEmitter
    {
        public List<AddressOperation> Operations { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) =>
            throw new NotSupportedException();

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) =>
            Operations.Add(operation);
    }

    private sealed class RecordingImplicitExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
