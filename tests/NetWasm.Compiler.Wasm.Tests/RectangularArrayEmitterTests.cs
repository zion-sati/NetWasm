using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class RectangularArrayEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddressCapabilityChecksEveryIndexAndEmitsTargetWidthAddress(bool memory64)
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
                exceptions));
        var arrayType = CliTypeIdentity.Array(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            2);
        var request = CreateInstructionRequest(
            CilOperation.LoadRectangularArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(arrayType),
            maxStack: 3);

        emitter.Emit(new(request, 0), GetCodeWriter(request));

        Assert.Equal(
            [
                ManagedExceptionKind.NullReference,
                ManagedExceptionKind.IndexOutOfRange,
                ManagedExceptionKind.IndexOutOfRange,
                ManagedExceptionKind.IndexOutOfRange,
                ManagedExceptionKind.IndexOutOfRange,
            ],
            exceptions.Kinds);
        Assert.Equal(
            memory64,
            GetCodeBytes(request).Contains(WasmOpcodes.I64ExtendI32Unsigned));
    }

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
                new RecordingExceptionEmitter()));
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
                new RecordingExceptionEmitter()));
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
        IInstructionCommandProvider emitter = new RectangularArrayElementStoreEmitter(
            layouts,
            CreateAddressInstructions(layouts),
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new RecordingExceptionEmitter(),
            addresses);

        Emit(emitter, request);

        Assert.Empty(request.Stack);
        Assert.Equal(0, addresses.Request?.ArraySlot);
        Assert.Contains(
            elementKind == CliValueKind.ValueType
                ? WasmOpcodes.Prefixed
                : elementKind == CliValueKind.ManagedReference
                    ? WasmOpcodes.Call
                    : WasmOpcodes.I32Store,
            GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.ManagedReference)]
    public void AllocationValidatesDimensionsPublishesRootAndCallsRuntime(
        CliValueKind elementKind)
    {
        var layouts = new RecordingLayoutProvider();
        var roots = new List<int>();
        var elementType = elementKind == CliValueKind.ManagedReference
            ? CliTypeIdentity.Named(Assembly, "Test", "Reference", false)
            : CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var arrayType = CliTypeIdentity.Array(elementType, 2);
        var context = CreateMethodEmissionContext(2) with
        {
            ValueLayout = new ValueFrameLayout(
                8,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 0),
                []),
        };
        var request = CreateInstructionRequest(
            CilOperation.NewRectangularArray,
            [CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(arrayType),
            context,
            2);
        IInstructionCommandProvider emitter = new RectangularArrayAllocationEmitter(
            layouts,
            CreateAddressInstructions(layouts),
            layouts,
            layouts,
            CreateTypeOperands(new FakeProgram()),
            WasmRuntimeImports.CreateCatalog(),
            new RecordingExceptionEmitter(),
            new RecordingRootPublicationEmitter(value => roots.Add(value.Instruction.Offset)));

        Emit(emitter, request);

        Assert.Equal([0], roots);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
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

    private sealed class RecordingRectangularAddressEmitter :
        IRectangularArrayElementAddressEmitter
    {
        public RectangularArrayElementAddressRequest? Request { get; private set; }

        public void Emit(
            RectangularArrayElementAddressRequest request,
            IWasmInstructionWriter code)
        {
            Request = request;
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(0)));
        }
    }

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
