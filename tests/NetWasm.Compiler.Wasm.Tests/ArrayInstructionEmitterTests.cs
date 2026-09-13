using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayInstructionEmitterTests
{
    [Fact]
    public void OwnsCompleteArrayInstructionFamily()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);

        var expected = (CilOperation[])
        [
            CilOperation.NewArray,
            CilOperation.LoadArrayLength,
            CilOperation.LoadArrayElementReference,
            CilOperation.StoreArrayElementReference,
            CilOperation.LoadArrayElement,
            CilOperation.LoadArrayElementAddress,
            CilOperation.StoreArrayElement,
            CilOperation.InitializeArrayData,
        ];
        Assert.Equal(expected, emitter.Commands.Select(command => command.Operation));
    }

    [Fact]
    public void NewArrayPublishesRootsAndProducesManagedReference()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out var safepoints);
        var request = CreateRequest(
            CilOperation.NewArray,
            [CliValueKind.I4],
            new CilOperand.Entity(TypeKey));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Single(safepoints);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void NewArrayUsesValueArrayAllocationForValueElements()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var elementType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var request = CreateRequest(
            CilOperation.NewArray,
            [CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void InitializeArrayDataWritesEachByteAndAdvancesAfterFirst()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var request = CreateRequest(
            CilOperation.InitializeArrayData,
            [CliValueKind.ManagedReference],
            new CilOperand.ByteData([0x01, 0x02]));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Empty(request.Stack);
        Assert.Equal(2, GetCodeBytes(request).Count(byteValue =>
            byteValue == WasmOpcodes.I32Store8));
    }

    [Fact]
    public void ArrayLengthReplacesReferenceWithInt32()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var request = CreateRequest(
            CilOperation.LoadArrayLength,
            [CliValueKind.ManagedReference]);

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
    }

    [Fact]
    public void ReferenceStorePerformsRuntimeAssignabilityCheck()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var request = CreateRequest(
            CilOperation.StoreArrayElementReference,
            [
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
            ]);

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void ReferenceLoadReplacesArrayAndIndexWithReference()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var request = CreateRequest(
            CilOperation.LoadArrayElementReference,
            [CliValueKind.ManagedReference, CliValueKind.I4]);

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
    }

    [Fact]
    public void ElementLoadUsesTypedLoadForReferenceElements()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var elementType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Reference",
            isValueType: false);
        var request = CreateRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
    }

    [Fact]
    public void ElementLoadCopiesValueElementsWithoutTemporaryOffset()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var elementType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(0, [], [],
                ImmutableDictionary<int, int>.Empty.Add(0, 0), [])
        };
        var request = CreateRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType),
            context);

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void ElementLoadCopiesValueElementsWithTemporaryOffset()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var elementType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(8, [], [],
                ImmutableDictionary<int, int>.Empty.Add(0, 8), [])
        };
        var request = CreateRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType),
            context);

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void ElementAddressLoadReplacesArrayAndIndexWithAddress()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var elementType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var request = CreateRequest(
            CilOperation.LoadArrayElementAddress,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
    }

    [Fact]
    public void ElementStoreCopiesValueElementsWithMemoryCopy()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var elementType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var request = CreateRequest(
            CilOperation.StoreArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ValueType],
            new CilOperand.TypeIdentity(elementType));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void ElementStoreUsesTypedStoreForScalarElements()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var elementType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var request = CreateRequest(
            CilOperation.StoreArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
    }

    private static void Emit<TProvider>(
        TProvider emitter,
        InstructionEmissionRequest request)
        where TProvider : IInstructionCommandProvider
    {
        var command = emitter.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());
    }

    private static ArrayInstructionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        out List<int> safepoints)
    {
        safepoints = [];
        var observedSafepoints = safepoints;
        IImplicitExceptionEmitter exceptions = new ImplicitExceptionEmitter(layouts, layouts, 7);
        IAddressInstructionEmitter addresses = new AddressInstructionEmitter(layouts);
        IRootPublicationEmitter roots = new RecordingRootPublicationEmitter(
            request => observedSafepoints.Add(request.Instruction.Offset));
        IArrayLengthAdapter lengths = new ArrayLengthAdapter(layouts, exceptions);
        return new ArrayInstructionEmitter(layouts, addresses, layouts, layouts, layouts, CreateTypeOperands(new FakeProgram()),
            WasmRuntimeImports.CreateCatalog(),
            exceptions,
            roots,
            lengths);
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        IEnumerable<CliValueKind> stack,
        CilOperand? operand = null,
        MethodEmissionContext? context = null) =>
        CreateInstructionRequest(operation, stack, operand, context);
}
