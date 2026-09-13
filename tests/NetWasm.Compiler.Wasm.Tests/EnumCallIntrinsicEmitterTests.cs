using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumCallIntrinsicEmitterTests
{
    [Fact]
    public void EqualityDelegatesThroughItsCapability()
    {
        var operation = new RecordingEqualsEmitter();
        var emitter = new EnumEqualsIntrinsicEmitter(operation);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.EnumEquals,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);

        EmitThroughCapability(emitter, request);

        Assert.True(operation.Called);
    }

    [Fact]
    public void HashCodeDelegatesThroughItsCapability()
    {
        var operation = new RecordingHashCodeEmitter();
        var emitter = new EnumGetHashCodeIntrinsicEmitter(operation);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.EnumGetHashCode,
            [CliValueKind.ManagedReference]);

        EmitThroughCapability(emitter, request);

        Assert.True(operation.Called);
    }

    [Fact]
    public void CompareToDelegatesThroughItsCapability()
    {
        var operation = new RecordingCompareToEmitter();
        var emitter = new EnumCompareToIntrinsicEmitter(operation);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.EnumCompareTo,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);

        EmitThroughCapability(emitter, request);

        Assert.True(operation.Called);
    }

    [Fact]
    public void RemainingEnumIntrinsicAdaptersDelegateThroughTheirCapabilities()
    {
        var operation = new RecordingEnumOperationEmitter();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.EnumConvert,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);
        IRuntimeIntrinsicEmitter[] emitters =
        [
            new EnumConvertIntrinsicEmitter(operation),
            new EnumGetNameIntrinsicEmitter(operation),
            new EnumGetNamesIntrinsicEmitter(operation),
            new EnumGetUnderlyingTypeIntrinsicEmitter(operation),
            new EnumGetValuesIntrinsicEmitter(operation),
            new EnumHasFlagIntrinsicEmitter(operation),
            new EnumIsDefinedIntrinsicEmitter(operation),
            new EnumParseIntrinsicEmitter(operation),
            new EnumToObjectIntrinsicEmitter(operation),
            new EnumToStringIntrinsicEmitter(operation),
            new EnumTypeCodeIntrinsicEmitter(operation, new RecordingValueReturnEmitter()),
        ];

        foreach (var emitter in emitters)
        {
            EmitThroughCapability(emitter, request);
        }

        Assert.Equal(emitters.Length, operation.Calls);
    }

    [Fact]
    public void TypeCodeForwardsTheConstrainedReceiverKindAndType()
    {
        var operation = new RecordingEnumOperationEmitter();
        var constrainedType = CliTypeIdentity.Named(
            new AssemblyIdentity("EnumTests"),
            "Tests",
            "State",
            isValueType: true);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.EnumGetTypeCode,
            [CliValueKind.ManagedAddress],
            constrainedType: constrainedType);

        new EnumTypeCodeIntrinsicEmitter(operation, new RecordingValueReturnEmitter()).Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));

        Assert.Equal(CliValueKind.ManagedAddress, operation.ReceiverKind);
        Assert.Equal(constrainedType, operation.ConstrainedType);
    }

    [Fact]
    public void HasFlagForwardsTheConstrainedReceiverKindAndType()
    {
        var operation = new RecordingEnumOperationEmitter();
        var constrainedType = CliTypeIdentity.Named(
            new AssemblyIdentity("EnumTests"),
            "Tests",
            "State",
            isValueType: true);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.EnumHasFlag,
            [CliValueKind.ManagedAddress, CliValueKind.ManagedReference],
            constrainedType: constrainedType);

        new EnumHasFlagIntrinsicEmitter(operation).Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));

        Assert.Equal(CliValueKind.ManagedAddress, operation.ReceiverKind);
        Assert.Equal(constrainedType, operation.ConstrainedType);
    }

    private sealed class RecordingEqualsEmitter : IEnumEqualsEmitter
    {
        public bool Called { get; private set; }

        public void EmitEquals(
            IWasmInstructionWriter code,
            CliValueKind leftKind,
            CliTypeIdentity? constrainedType,
            int left,
            int right,
            int result,
            int temporaryReference,
            int temporaryI4) =>
            Called = true;
    }

    private static void EmitThroughCapability(
        IRuntimeIntrinsicEmitter emitter,
        RuntimeIntrinsicEmissionRequest request) => emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));

    private sealed class RecordingHashCodeEmitter : IEnumHashCodeEmitter
    {
        public bool Called { get; private set; }

        public void EmitHashCode(
            IWasmInstructionWriter code,
            CliValueKind receiverKind,
            CliTypeIdentity? receiverType,
            int receiver,
            int result,
            int temporaryI4,
            int temporaryI8) => Called = true;
    }

    private sealed class RecordingCompareToEmitter : IEnumCompareToEmitter
    {
        public bool Called { get; private set; }

        public void EmitCompareTo(
            IWasmInstructionWriter code,
            CliValueKind leftKind,
            CliTypeIdentity? constrainedType,
            int left,
            int right,
            int result,
            int temporaryReference,
            int temporaryI4) =>
            Called = true;
    }

    private sealed class RecordingEnumOperationEmitter :
        IEnumConvertEmitter,
        IEnumGetNameEmitter,
        IEnumGetNamesEmitter,
        IEnumGetUnderlyingTypeEmitter,
        IEnumGetValuesEmitter,
        IEnumHasFlagEmitter,
        IEnumIsDefinedEmitter,
        IEnumParseEmitter,
        IEnumToObjectEmitter,
        IEnumToStringEmitter,
        IEnumTypeCodeEmitter
    {
        public int Calls { get; private set; }
        public CliValueKind ReceiverKind { get; private set; }
        public CliTypeIdentity? ConstrainedType { get; private set; }

        public void EmitConvert(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitGetName(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitGetNames(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitGetUnderlyingType(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitGetValues(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitHasFlag(
            IWasmInstructionWriter code,
            int receiver,
            int flag,
            int result,
            int temporaryI4,
            CliValueKind receiverKind = CliValueKind.ManagedReference,
            CliTypeIdentity? constrainedType = null)
        {
            Calls++;
            ReceiverKind = receiverKind;
            ConstrainedType = constrainedType;
        }

        public void EmitIsDefined(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitParse(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitToObject(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitToString(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;

        public void EmitTypeCode(
            IWasmInstructionWriter code,
            int receiver,
            int result,
            int temporaryI4,
            CliTypeIdentity? constrainedType = null,
            CliValueKind receiverKind = CliValueKind.ManagedReference)
        {
            Calls++;
            ReceiverKind = receiverKind;
            ConstrainedType = constrainedType;
        }
    }

    private sealed class RecordingValueReturnEmitter : IEnumValueReturnEmitter
    {
        public void Emit(
            IWasmInstructionWriter code,
            RuntimeIntrinsicEmissionRequest request,
            int valueLocal)
        {
        }
    }
}
