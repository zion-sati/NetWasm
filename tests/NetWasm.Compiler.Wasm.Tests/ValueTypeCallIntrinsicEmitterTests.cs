using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ValueTypeCallIntrinsicEmitterTests
{
    private static readonly CliTypeIdentity StructType = CliTypeIdentity.Named(
        new AssemblyIdentity("ValueTypeTests"),
        "Tests",
        "Sample",
        isValueType: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntrinsicAdaptersRequireAndForwardConstrainedType(bool hashCode)
    {
        var operation = new RecordingOperation();
        IRuntimeIntrinsicEmitter emitter = hashCode
            ? new ValueTypeGetHashCodeIntrinsicEmitter(operation)
            : new ValueTypeEqualsIntrinsicEmitter(operation);
        var intrinsic = hashCode
            ? RuntimeIntrinsic.ValueTypeGetHashCode
            : RuntimeIntrinsic.ValueTypeEquals;
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic,
            hashCode
                ? [CliValueKind.ManagedAddress]
                : [CliValueKind.ManagedAddress, CliValueKind.ManagedReference],
            constrainedType: StructType);

        emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));

        Assert.Equal(StructType, operation.Type);
        Assert.Equal(CliValueKind.ManagedAddress, operation.ReceiverKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntrinsicAdaptersRejectMissingConstrainedType(bool hashCode)
    {
        var operation = new RecordingOperation();
        IRuntimeIntrinsicEmitter emitter = hashCode
            ? new ValueTypeGetHashCodeIntrinsicEmitter(operation)
            : new ValueTypeEqualsIntrinsicEmitter(operation);
        var intrinsic = hashCode
            ? RuntimeIntrinsic.ValueTypeGetHashCode
            : RuntimeIntrinsic.ValueTypeEquals;
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic,
            hashCode
                ? [CliValueKind.ManagedAddress]
                : [CliValueKind.ManagedAddress, CliValueKind.ManagedReference]);

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction)));
        Assert.Null(operation.Type);
    }

    private sealed class RecordingOperation :
        IValueTypeEqualsEmitter,
        IValueTypeHashCodeEmitter
    {
        public CliTypeIdentity? Type { get; private set; }
        public CliValueKind ReceiverKind { get; private set; }

        public void Emit(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code,
            CliTypeIdentity type,
            CliValueKind receiverKind)
        {
            Type = type;
            ReceiverKind = receiverKind;
        }
    }
}
