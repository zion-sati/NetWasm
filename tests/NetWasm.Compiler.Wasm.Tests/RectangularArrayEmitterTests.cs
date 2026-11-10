using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class RectangularArrayEmitterTests
{
    // A caller that has checked the array (a store, before reading its element type)
    // leaves out only the null check.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AddressCapabilityChecksEveryIndexAndEmitsTargetWidthAddress(bool memory64, bool arrayChecked)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var exceptions = new RecordingExceptionEmitter();
        var emitter = ThroughAddressContract(
            new RectangularArrayElementAddressEmitter(
                layouts,
                layouts,
                layouts,
                CreateAddressInstructions(layouts),
                layouts,
                layouts,
                exceptions,
                WasmRuntimeImports.CreateCatalog()));
        var arrayType = CliTypeIdentity.Array(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            2);
        var request = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(arrayType),
            maxStack: 3);

        emitter.Emit(new(request, 0, arrayChecked), GetCodeWriter(request));

        Assert.Equal(
            arrayChecked
                ? [ManagedExceptionKind.IndexOutOfRange, ManagedExceptionKind.IndexOutOfRange]
                : [
                    ManagedExceptionKind.NullReference,
                    ManagedExceptionKind.IndexOutOfRange,
                    ManagedExceptionKind.IndexOutOfRange,
                ],
            exceptions.Kinds);
        Assert.Equal(
            memory64,
            GetCodeBytes(request).Contains(WasmOpcodes.I64ExtendI32Unsigned));
    }

    // An element access of rank two or more reads its lower bounds from the
    // shape; a call per dimension took 89% of a matrix addition's time. A
    // rank-one ARRAY still asks the runtime, which tells it from an SZARRAY.
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void OnlyRankOneAddressAsksTheRuntimeForALowerBound(int rank, bool asksRuntime)
    {
        var layouts = new RecordingLayoutProvider();
        var imports = new RecordingRuntimeImports();
        var emitter = new RectangularArrayElementAddressEmitter(
            layouts,
            layouts,
            layouts,
            CreateAddressInstructions(layouts),
            layouts,
            layouts,
            new RecordingExceptionEmitter(),
            imports);
        var arrayType = CliTypeIdentity.Array(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            rank);
        var request = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, .. Enumerable.Repeat(CliValueKind.I4, rank)],
            new CilOperand.TypeIdentity(arrayType),
            maxStack: rank + 1);

        emitter.Emit(new(request, 0), GetCodeWriter(request));

        Assert.Equal(asksRuntime, imports.Symbols.Contains(RuntimeImportSymbol.ArrayGetLowerBound));
    }

    // Elements are stored row-major, so the offset multiplies by each later
    // dimension's length, which the bounds check reads, and never by a stride.
    [Fact]
    public void AddressReadsLengthsAndNoStrides()
    {
        var layouts = new RecordingLayoutProvider();
        var request = RankTwoRequest(CreateMethodEmissionContext());

        CreateAddressEmitter(layouts).Emit(new(request, 0), GetCodeWriter(request));

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        Assert.DoesNotContain(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, sizeof(int))),
            instructions);
    }

    // An array the method's loops snapshot has its lower bounds, lengths and
    // data in locals, so its element address reads no memory.
    [Fact]
    public void CachedAddressReadsTheSnapshotInsteadOfTheShape()
    {
        var layouts = new RecordingLayoutProvider();
        var slot = new ArrayReferenceSlot(true, 0);
        var request = RankTwoRequest(CreateMethodEmissionContext() with
        {
            ArrayMetadata = new(
                new ArrayMetadataCachingPlan([slot], ImmutableDictionary<int, ArrayReferenceSlot>.Empty.Add(0, slot))
                {
                    Ranks = ImmutableDictionary<ArrayReferenceSlot, int>.Empty.Add(slot, 2),
                },
                [])
            {
                RectangularSlots = ImmutableDictionary<ArrayReferenceSlot, RectangularArraySnapshotLocals>.Empty
                    .Add(slot, new([new(30, 31), new(32, 33)], 34)),
            },
        });

        CreateAddressEmitter(layouts).Emit(new(request, 0), GetCodeWriter(request));

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Load);
        Assert.All([30, 31, 32, 33, 34], local => Assert.Contains(
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)), instructions));
    }

    private static RectangularArrayElementAddressEmitter CreateAddressEmitter(RecordingLayoutProvider layouts) =>
        new RectangularArrayElementAddressEmitter(
            layouts,
            layouts,
            layouts,
            CreateAddressInstructions(layouts),
            layouts,
            layouts,
            new RecordingExceptionEmitter(),
            WasmRuntimeImports.CreateCatalog());

    private static InstructionEmissionRequest RankTwoRequest(MethodEmissionContext context) =>
        CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(CliTypeIdentity.Array(CliTypeIdentity.Primitive("i4", CliValueKind.I4), 2)),
            context,
            maxStack: 3);

    [Fact]
    public void AddressCapabilityRejectsMissingArrayTypeOperand()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = ThroughAddressContract(
            new RectangularArrayElementAddressEmitter(
                layouts,
                layouts,
                layouts,
                CreateAddressInstructions(layouts),
                layouts,
                layouts,
                new RecordingExceptionEmitter(),
                WasmRuntimeImports.CreateCatalog()));
        var request = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            emitter.Emit(new(request, 0), GetCodeWriter(request)));

        Assert.Contains("array type operand", exception.Message);
    }

    [Fact]
    public void AddressCapabilityUsesReferenceWidthForReferenceElements()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = ThroughAddressContract(
            new RectangularArrayElementAddressEmitter(
                layouts,
                layouts,
                layouts,
                CreateAddressInstructions(layouts),
                layouts,
                layouts,
                new RecordingExceptionEmitter(),
                WasmRuntimeImports.CreateCatalog()));
        var arrayType = CliTypeIdentity.Array(
            CliTypeIdentity.Named(Assembly, "Test", "Reference", false),
            2);
        var request = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(arrayType),
            maxStack: 3);

        emitter.Emit(new(request, 0), GetCodeWriter(request));

        Assert.Contains(WasmOpcodes.I32Multiply, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void RankOneReferenceAddressUsesSharedArrayHeaderAndReadonlyPolicy(
        bool memory64,
        bool hasPrefix,
        bool isReadonly)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var exceptions = new RecordingExceptionEmitter();
        var emitter = ThroughAddressContract(
            new RectangularArrayElementAddressEmitter(
                layouts,
                layouts,
                layouts,
                CreateAddressInstructions(layouts),
                layouts,
                layouts,
                exceptions,
                WasmRuntimeImports.CreateCatalog()));
        var arrayType = CliTypeIdentity.Array(
            CliTypeIdentity.Named(Assembly, "Test", "Reference", false),
            1);
        var original = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElementAddress,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.TypeIdentity(arrayType),
            maxStack: 2);
        var writer = new RecordingInstructionWriter();
        var request = original with
        {
            Header = original.Header with
            {
                Instructions = hasPrefix
                    ?
                    [
                        I(-2, CilOperation.Nop),
                        I(-1, isReadonly ? CilOperation.Readonly : CilOperation.Nop),
                        original.Instruction,
                    ]
                    :
                    [
                        I(-2, CilOperation.Nop),
                        I(-1, CilOperation.Nop),
                    ],
            },
        };
        RegisterInstructionWriter(request, writer);

        emitter.Emit(new(request, 0), GetCodeWriter(request));

        Assert.Equal(
            isReadonly
                ? [
                    ManagedExceptionKind.NullReference,
                    ManagedExceptionKind.IndexOutOfRange,
                ]
                : [
                    ManagedExceptionKind.NullReference,
                    ManagedExceptionKind.ArrayTypeMismatch,
                    ManagedExceptionKind.IndexOutOfRange,
                ],
            exceptions.Kinds);
        Assert.Equal(
            memory64,
            GetCodeBytes(request).Contains(WasmOpcodes.I64ExtendI32Unsigned));
    }

    [Theory]
    [InlineData(CilOperation.LoadRectangularArrayElement, CliValueKind.I4)]
    [InlineData(CilOperation.LoadRectangularArrayElementAddress, CliValueKind.ManagedAddress)]
    public void ElementCommandsDelegateAddressingAndPublishResult(
        CilOperation operation,
        CliValueKind expected)
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new RecordingRectangularAddressEmitter();
        var arrayType = CliTypeIdentity.Array(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            2);
        var request = CreateInstructionRequest(
            operation,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(arrayType),
            maxStack: 3);
        IInstructionCommandProvider emitter = operation switch
        {
            CilOperation.LoadRectangularArrayElement =>
                new RectangularArrayElementLoadEmitter(layouts, layouts, addresses),
            _ => new RectangularArrayElementAddressInstructionEmitter(layouts, addresses),
        };

        Emit(emitter, request);

        Assert.Equal(0, addresses.Request?.ArraySlot);
        Assert.False(addresses.Request?.ArrayChecked);
        Assert.Equal([expected], request.Stack);
    }

    [Fact]
    public void ReferenceElementLoadUsesReferenceLoad()
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new RecordingRectangularAddressEmitter();
        var arrayType = CliTypeIdentity.Array(
            CliTypeIdentity.Named(Assembly, "Test", "Reference", false),
            2);
        var request = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(arrayType),
            maxStack: 3);
        IInstructionCommandProvider emitter =
            new RectangularArrayElementLoadEmitter(layouts, layouts, addresses);

        Emit(emitter, request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void ValueElementLoadCopiesFromArrayStorage(bool memory64, int offset)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var addresses = new RecordingRectangularAddressEmitter();
        var valueType = CliTypeIdentity.Named(Assembly, "Test", "Value", true);
        var arrayType = CliTypeIdentity.Array(valueType, 2);
        var context = CreateMethodEmissionContext(3) with
        {
            ValueLayout = new ValueFrameLayout(
                offset,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, offset),
                []),
        };
        var request = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(arrayType),
            context,
            3);
        IInstructionCommandProvider emitter =
            new RectangularArrayElementLoadEmitter(layouts, layouts, addresses);

        Emit(emitter, request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateValueTransferUsesValidatedAddressWithoutSnapshot(bool memory64)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var addresses = new RecordingRectangularAddressEmitter(memory64);
        var value = CliTypeIdentity.Named(Assembly, "Test", "Value", true);
        var context = CreateMethodEmissionContext(3) with
        {
            ValueLayout = new ValueFrameLayout(0, [], [], [], [])
            {
                DirectValueLoadOffsets = [0],
            },
        };
        var request = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(CliTypeIdentity.Array(value, 2)),
            context,
            3);
        IInstructionCommandProvider emitter =
            new RectangularArrayElementLoadEmitter(layouts, layouts, addresses);

        Emit(emitter, request);

        Assert.NotNull(addresses.Request);
        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.DoesNotContain(WasmOpcodes.Prefixed, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.LocalSet, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CliValueKind.I4, false)]
    [InlineData(CliValueKind.ValueType, false)]
    [InlineData(CliValueKind.ValueType, true)]
    [InlineData(CliValueKind.ManagedReference, false)]
    public void ElementStoreHandlesScalarValueAndReferenceElements(
        CliValueKind elementKind,
        bool memory64)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var addresses = new RecordingRectangularAddressEmitter();
        var elementType = elementKind == CliValueKind.ValueType
            ? CliTypeIdentity.Named(Assembly, "Test", "Value", true)
            : elementKind == CliValueKind.ManagedReference
                ? CliTypeIdentity.Named(Assembly, "Test", "Reference", false)
                : CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var arrayType = CliTypeIdentity.Array(elementType, 2);
        var request = CreateInstructionRequest(
            CilOperation.StoreRectangularArrayElement,
            [
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
                elementKind,
            ],
            new CilOperand.TypeIdentity(arrayType),
            maxStack: 4);
        var exceptions = new RecordingExceptionEmitter();
        IInstructionCommandProvider emitter = new RectangularArrayElementStoreEmitter(
            layouts,
            CreateAddressInstructions(layouts),
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            exceptions,
            addresses);

        Emit(emitter, request);

        Assert.Empty(request.Stack);
        Assert.Equal(0, addresses.Request?.ArraySlot);
        // The store checks the array once and tells the address it is checked.
        Assert.True(addresses.Request?.ArrayChecked);
        Assert.Equal(1, exceptions.Kinds.Count(kind => kind == ManagedExceptionKind.NullReference));
        Assert.Contains(
            elementKind == CliValueKind.ValueType
                ? WasmOpcodes.Prefixed
                : elementKind == CliValueKind.ManagedReference
                    ? WasmOpcodes.Call
                    : WasmOpcodes.I32Store,
            GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CliValueKind.I4, false)]
    [InlineData(CliValueKind.ManagedReference, false)]
    [InlineData(CliValueKind.I4, true)]
    [InlineData(CliValueKind.ManagedReference, true)]
    public void AllocationValidatesDimensionsPublishesRootAndCallsRuntime(
        CliValueKind elementKind,
        bool bounded)
    {
        var layouts = new RecordingLayoutProvider();
        var elementType = elementKind == CliValueKind.ManagedReference
            ? CliTypeIdentity.Named(Assembly, "Test", "Reference", false)
            : CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var arrayType = CliTypeIdentity.Array(elementType, 2);
        var argumentCount = bounded ? 4 : 2;
        var context = CreateMethodEmissionContext(argumentCount) with
        {
            ValueLayout = new ValueFrameLayout(
                argumentCount * sizeof(int),
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 0),
                []),
        };
        var request = CreateInstructionRequest(
            bounded ? CilOperation.NewBoundedRectangularArray : CilOperation.NewRectangularArray,
            [.. Enumerable.Repeat(CliValueKind.I4, argumentCount)],
            new CilOperand.TypeIdentity(arrayType),
            context,
            argumentCount);
        var exceptions = new RecordingExceptionEmitter();
        IInstructionCommandProvider emitter = new RectangularArrayAllocationEmitter(
            layouts,
            CreateAddressInstructions(layouts),
            layouts,
            layouts,
            CreateTypeOperands(new FakeProgram()),
            WasmRuntimeImports.CreateCatalog(),
            exceptions);

        Emit(emitter, request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(bounded ? 2 : 0,
            exceptions.Kinds.Count(kind => kind == ManagedExceptionKind.ArgumentOutOfRange));
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    private static void Emit(
        IInstructionCommandProvider emitter,
        InstructionEmissionRequest request)
    {
        var command = emitter.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());
    }

    private static IRectangularArrayElementAddressEmitter ThroughAddressContract(
        IRectangularArrayElementAddressEmitter emitter) => emitter;

    private sealed class RecordingRectangularAddressEmitter(bool memory64 = false) :
        IRectangularArrayElementAddressEmitter
    {
        public RectangularArrayElementAddressRequest? Request { get; private set; }

        public void Emit(
            RectangularArrayElementAddressRequest request,
            IWasmInstructionWriter code)
        {
            Request = request;
            code.Write(WasmInstruction.WithOperand(
                memory64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(0)));
        }
    }

    private sealed class RecordingRuntimeImports : IRuntimeImportResolver
    {
        private readonly RuntimeImportCatalog _catalog = WasmRuntimeImports.CreateCatalog();

        public List<RuntimeImportSymbol> Symbols { get; } = [];

        public int Resolve(RuntimeImportSymbol symbol)
        {
            Symbols.Add(symbol);
            return _catalog.Resolve(symbol);
        }

        public int Resolve(RuntimeImportSymbol symbol, WasmModuleProfile profile)
        {
            Symbols.Add(symbol);
            return _catalog.Resolve(symbol, profile);
        }

        public ImmutableArray<WasmFunctionImport> Resolve(WasmModuleProfile profile) => _catalog.Resolve(profile);
    }

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) =>
            Kinds.Add(kind);
    }
}
