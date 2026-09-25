using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayDimensionIntrinsicEmitterTests
{
    [Fact]
    public void GetLowerBoundValidatesDimensionWithoutTreatingNegativeBoundsAsErrors()
    {
        var receivers = new RecordingReceiverValidator();
        var exceptions = new RecordingExceptionEmitter();
        var emitter = ThroughIntrinsicContract(new ArrayGetLowerBoundIntrinsicEmitter(
            receivers, WasmRuntimeImports.CreateCatalog(), exceptions));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayGetLowerBound,
            [CliValueKind.ManagedReference, CliValueKind.I4]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        Assert.NotNull(receivers.Local);
        Assert.Equal([ManagedExceptionKind.IndexOutOfRange], exceptions.Kinds);
        Assert.Contains(WasmOpcodes.I32GreaterThanOrEqualUnsigned, GetCodeBytes(request.Instruction));
        Assert.DoesNotContain(WasmOpcodes.I32LessThanSigned, GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void ReceiverValidatorChecksForNullThroughItsCapability()
    {
        var addresses = new RecordingAddressEmitter();
        var exceptions = new RecordingExceptionEmitter();
        var validator = ThroughReceiverContract(
            new ArrayReceiverValidator(exceptions, addresses));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayRank,
            [CliValueKind.ManagedReference]);

        validator.Validate(GetCodeWriter(request.Instruction), 3);

        Assert.Equal([AddressOperation.EqualZero], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.NullReference], exceptions.Kinds);
    }

    [Fact]
    public void RankValidatesReceiverAndCallsRuntimeImport()
    {
        var receivers = new RecordingReceiverValidator();
        var emitter = ThroughIntrinsicContract(new ArrayRankIntrinsicEmitter(
            receivers,
            WasmRuntimeImports.CreateCatalog()));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayRank,
            [CliValueKind.ManagedReference]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        Assert.NotNull(receivers.Local);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void GetLengthValidatesReceiverAndRejectsInvalidDimensionResult()
    {
        var receivers = new RecordingReceiverValidator();
        var exceptions = new RecordingExceptionEmitter();
        var emitter = ThroughIntrinsicContract(new ArrayGetLengthIntrinsicEmitter(
            receivers,
            WasmRuntimeImports.CreateCatalog(),
            exceptions));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayGetLength,
            [CliValueKind.ManagedReference, CliValueKind.I4]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        Assert.NotNull(receivers.Local);
        Assert.Equal([ManagedExceptionKind.IndexOutOfRange], exceptions.Kinds);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
    }

    private static IArrayReceiverValidator ThroughReceiverContract(
        IArrayReceiverValidator validator) => validator;

    private static IRuntimeIntrinsicEmitter ThroughIntrinsicContract(
        IRuntimeIntrinsicEmitter emitter) => emitter;

    private sealed class RecordingReceiverValidator : IArrayReceiverValidator
    {
        public int? Local { get; private set; }

        public void Validate(IWasmInstructionWriter code, int receiverLocal) =>
            Local = receiverLocal;
    }

    private sealed class RecordingAddressEmitter : IAddressInstructionEmitter
    {
        public List<AddressOperation> Operations { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) =>
            throw new NotSupportedException();

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) =>
            Operations.Add(operation);
    }

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
