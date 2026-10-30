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
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

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
    public void InitializeArrayDataStoresSmallDataAsConstants()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts, out _);
        var request = CreateRequest(
            CilOperation.InitializeArrayData,
            [CliValueKind.ManagedReference],
            Initializer([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Empty(request.Stack);
        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        Assert.Equal(
            [WasmOpcodes.I64Store, WasmOpcodes.I32Store16, WasmOpcodes.I32Store8],
            instructions.Select(instruction => instruction.Opcode)
                .Where(opcode => opcode is WasmOpcodes.I64Store or WasmOpcodes.I32Store or WasmOpcodes.I32Store16 or
                    WasmOpcodes.I32Store8));
        Assert.Contains(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0x0807060504030201)),
            instructions);
        Assert.Null(layouts.StaticFieldRequest);
    }

    [Fact]
    public void InitializeArrayDataCopiesLargeDataFromItsField()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts, out _);
        var request = CreateRequest(
            CilOperation.InitializeArrayData,
            [CliValueKind.ManagedReference],
            Initializer([.. Enumerable.Range(0, 65).Select(static value => (byte)value)]));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Empty(request.Stack);
        Assert.Equal(new EntityKey(Assembly, 0x04000009), layouts.StaticFieldRequest);
        Assert.DoesNotContain(WasmOpcodes.I32Store8, GetCodeBytes(request));
        Assert.Contains(WasmInstruction.WithOperand(WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)),
            ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions());
    }

    private static CilOperand.ArrayInitializerData Initializer(ImmutableArray<byte> data)
    {
        var owner = new EntityKey(Assembly, 0x02000009);
        var field = new FieldDefinitionModel(new(Assembly, 0x04000009), owner, "Data", CliValueKind.ValueType, true)
        {
            InitialData = data,
        };
        return new(new FieldInstanceModel(field, CliTypeIdentity.Named(Assembly, "Test", "Owner", isValueType: false),
            field.SignatureType));
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
    public void LengthLeavesTheNullCheckToTheLoopEntryThatMadeIt()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var slot = new ArrayReferenceSlot(false, 0);
        var request = CreateRequest(
            CilOperation.LoadArrayLength,
            [CliValueKind.ManagedReference],
            context: CreateMethodEmissionContext() with
            {
                ArrayMetadata = new(
                    new ArrayMetadataCachingPlan([slot], ImmutableDictionary<int, ArrayReferenceSlot>.Empty.Add(0, slot))
                    {
                        CheckedAccesses = [0],
                    },
                    ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals>.Empty.Add(slot, new(30, 31))),
            });

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.DoesNotContain(WasmOpcodes.If, GetCodeBytes(request));
        Assert.Contains(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(30)),
            ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions());
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

    // A value of exactly the array's element type, or any value stored into an
    // object[], skips the runtime's hierarchy check, as .NET's store helper does.
    [Fact]
    public void ReferenceStoreAcceptsTheElementTypeOrObjectBeforeTheHierarchyCheck()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts, out _);
        var request = CreateRequest(
            CilOperation.StoreArrayElementReference,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedReference]);

        Emit((IInstructionCommandProvider)emitter, request);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        var isAssignable = WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
            (uint)WasmRuntimeImports.CreateCatalog().Resolve(RuntimeImportSymbol.IsAssignable)));
        var call = Array.IndexOf(instructions.ToArray(), isAssignable);
        var fastPath = instructions[..call].ToArray();
        var headerLoad = Array.IndexOf(fastPath, WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        Assert.True(headerLoad >= 0);
        var objectTypeId = layouts.GetObjectLayout(
            CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, isValueType: false)).TypeId;
        Assert.Equal(
            [WasmOpcodes.I32NotEqual, WasmOpcodes.I32Constant, WasmOpcodes.I32NotEqual, WasmOpcodes.I32And, WasmOpcodes.If],
            fastPath[(headerLoad + 1)..].Select(static instruction => instruction.Opcode)
                .Where(static opcode => opcode is not (WasmOpcodes.LocalGet or WasmOpcodes.I32Load)));
        Assert.Contains(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(objectTypeId)), fastPath);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateValueTransferKeepsValidationWithoutSnapshot(bool memory64)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var exceptions = new RecordingArrayExceptions();
        var emitter = CreateEmitter(layouts, out _, exceptions);
        var value = CliTypeIdentity.Named(Assembly, "Test", "Value", true);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(0, [], [], [], [])
            {
                DirectValueLoadOffsets = [0],
            },
        };
        var request = CreateRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(value),
            context);
        var copied = CreateRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(value),
            context with
            {
                ValueLayout = new ValueFrameLayout(8, [], [],
                    ImmutableDictionary<int, int>.Empty.Add(0, 8), []),
            });

        Emit((IInstructionCommandProvider)emitter, request);
        var directExceptions = exceptions.Kinds.ToArray();
        exceptions.Kinds.Clear();
        Emit((IInstructionCommandProvider)emitter, copied);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        var code = GetCodeBytes(request);
        Assert.DoesNotContain(WasmOpcodes.Prefixed, code);
        Assert.Contains(ManagedExceptionKind.NullReference, directExceptions);
        Assert.Contains(ManagedExceptionKind.IndexOutOfRange, directExceptions);
        Assert.Equal(exceptions.Kinds, directExceptions);
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

    [Fact]
    public void GenericReferenceElementStorePerformsRuntimeElementCheck()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(), out _);
        var elementType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Reference",
            isValueType: false);
        var request = CreateRequest(
            CilOperation.StoreArrayElement,
            [
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
            ],
            new CilOperand.TypeIdentity(elementType));

        Emit((IInstructionCommandProvider)emitter, request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void MutableReferenceElementAddressChecksExactElementTypeUnlessReadonly()
    {
        var elementType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Reference",
            isValueType: false);
        var mutableBase = CreateRequest(
            CilOperation.LoadArrayElementAddress,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType));
        var mutableWriter = new RecordingInstructionWriter();
        var mutable = mutableBase with
        {
            Header = mutableBase.Header with
            {
                Instructions =
                [
                    I(-2, CilOperation.Nop),
                    I(-1, CilOperation.Nop),
                    mutableBase.Instruction,
                ],
            },
        };
        RegisterInstructionWriter(mutable, mutableWriter);
        var missingBase = CreateRequest(
            CilOperation.LoadArrayElementAddress,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType));
        var missingWriter = new RecordingInstructionWriter();
        var missing = missingBase with
        {
            Header = missingBase.Header with
            {
                Instructions =
                [
                    I(-2, CilOperation.Nop),
                    I(-1, CilOperation.Nop),
                ],
            },
        };
        RegisterInstructionWriter(missing, missingWriter);
        var readonlyBase = CreateRequest(
            CilOperation.LoadArrayElementAddress,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(elementType));
        var readonlyWriter = new RecordingInstructionWriter();
        var readonlyRequest = readonlyBase with
        {
            Header = readonlyBase.Header with
            {
                Instructions =
                [
                    I(-1, CilOperation.Readonly),
                    readonlyBase.Instruction,
                ],
            },
        };
        RegisterInstructionWriter(readonlyRequest, readonlyWriter);

        Emit((IInstructionCommandProvider)CreateEmitter(
            new RecordingLayoutProvider(),
            out _), mutable);
        Emit((IInstructionCommandProvider)CreateEmitter(
            new RecordingLayoutProvider(),
            out _), readonlyRequest);
        Emit((IInstructionCommandProvider)CreateEmitter(
            new RecordingLayoutProvider(),
            out _), missing);

        Assert.Equal(
            CountManagedThrows(GetCodeBytes(readonlyRequest)) + 1,
            CountManagedThrows(GetCodeBytes(mutable)));
        Assert.Equal(
            CountManagedThrows(GetCodeBytes(mutable)),
            CountManagedThrows(GetCodeBytes(missing)));
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
        out List<int> safepoints,
        IImplicitExceptionEmitter? exceptionEmitter = null)
    {
        safepoints = [];
        var observedSafepoints = safepoints;
        IImplicitExceptionEmitter exceptions = exceptionEmitter ??
            new ImplicitExceptionEmitter(layouts, layouts);
        IAddressInstructionEmitter addresses = new AddressInstructionEmitter(layouts);
        IRootPublicationEmitter roots = new RecordingRootPublicationEmitter(
            request => observedSafepoints.Add(request.Instruction.Offset));
        IArrayLengthAdapter lengths = new ArrayLengthAdapter(layouts, exceptions);
        IArrayIndexAdapter indexes = new ArrayIndexAdapter(
            layouts,
            addresses,
            layouts,
            exceptions);
        return new ArrayInstructionEmitter(layouts, addresses, layouts, layouts, layouts, CreateTypeOperands(new FakeProgram()),
            WasmRuntimeImports.CreateCatalog(),
            exceptions,
            roots,
            lengths,
            indexes,
            new ArrayElementAddressEmitter(layouts, addresses),
            layouts);
    }

    private sealed class RecordingArrayExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) =>
            Kinds.Add(kind);
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        IEnumerable<CliValueKind> stack,
        CilOperand? operand = null,
        MethodEmissionContext? context = null) =>
        CreateInstructionRequest(operation, stack, operand, context);
}
