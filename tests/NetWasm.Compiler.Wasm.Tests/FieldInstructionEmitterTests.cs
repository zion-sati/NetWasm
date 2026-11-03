using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class FieldInstructionEmitterTests
{
    [Theory]
    [InlineData(CilOperation.LoadField, 1)]
    [InlineData(CilOperation.StoreField, 2)]
    public void SelectedAggregateProjectionDelegatesWithoutEmittingAnotherTransfer(CilOperation operation, int stackSize)
    {
        var request = CreateRequest(operation, stackSize);
        var projection = new SelectedProjection();

        Emit(CreateEmitter(projections: projection), request);

        Assert.Same(request, projection.Request);
        Assert.Same(GetCodeWriter(request), projection.Writer);
        Assert.Empty(GetCodeBytes(request));
        Assert.Equal(stackSize, request.Stack.Count);
    }

    private sealed class SelectedProjection : IAddressTakenAggregateProjectionEmitter
    {
        public InstructionEmissionRequest? Request { get; private set; }
        public IWasmInstructionWriter? Writer { get; private set; }
        public bool TryEmit(InstructionEmissionRequest request, IWasmInstructionWriter code)
        {
            Request = request;
            Writer = code;
            return true;
        }
    }

    [Theory]
    [InlineData(CilOperation.LoadField, 1, CliValueKind.I4)]
    [InlineData(CilOperation.LoadFieldAddress, 1, CliValueKind.ManagedAddress)]
    public void LoadOperationProducesExpectedStackType(
        CilOperation operation,
        int inputCount,
        CliValueKind expected)
    {
        var request = CreateRequest(operation, inputCount);

        Emit(CreateEmitter(), request);

        Assert.Equal([expected], request.Stack);
        Assert.NotEmpty(GetCodeBytes(request));
    }

    // A field access whose object an earlier access checked reads it unchecked,
    // unless the proof describes another snapshot of the body.
    [Theory]
    [InlineData(CilOperation.LoadField, 1, false)]
    [InlineData(CilOperation.StoreField, 2, false)]
    [InlineData(CilOperation.LoadField, 1, true)]
    public void AccessSkipsOnlyTheNullCheckAnEarlierAccessProved(CilOperation operation, int stackSize, bool stale)
    {
        var request = CreateRequest(operation, stackSize);
        var writer = (RecordingInstructionWriter)GetCodeWriter(request);
        var header = request.Header with { Instructions = [request.Instruction] };
        header = header with
        {
            ArrayBoundsProof = new(header.Instructions, header.Locals, header.LocalSignatureTypes, [])
            {
                NonNullOffsets = [request.Instruction.Offset],
            },
        };
        if (stale) header = header with { Instructions = [request.Instruction with { OriginalOffset = 123 }] };
        request = request with { Header = header };

        AsProvider(CreateEmitter()).Commands.Single(command => command.Operation == operation)
            .Emit(request, writer, CreateFunctionIndexResolver());

        Assert.Equal(stale, writer.ToInstructions().Any(instruction => instruction.Opcode == WasmOpcodes.If));
    }

    [Fact]
    public void StoreConsumesReceiverAndValue()
    {
        var request = CreateRequest(CilOperation.StoreField, 2);

        Emit(CreateEmitter(), request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CilOperation.LoadField)]
    [InlineData(CilOperation.LoadFieldAddress)]
    [InlineData(CilOperation.StoreField)]
    public void Memory64ValueFieldsReadTheActualNativePointerLocal(CilOperation operation)
    {
        var fieldType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = CreateField(fieldType);
        var layouts = new FieldLayoutScenario(
            WasmTargetLayout.Wasm64,
            new FieldLayout(0) { Size = 4, Type = fieldType });
        var context = CreateMethodEmissionContext();
        var stack = operation == CilOperation.StoreField
            ? [CliValueKind.NativeInt, CliValueKind.I4]
            : (CliValueKind[])[CliValueKind.NativeInt];
        var request = CreateRequest(
            operation,
            stack,
            new CilOperand.Entity(InstanceFieldKey),
            context);

        Emit(CreateEmitter(new FieldProgram(field, true), layouts), request);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        var nativeLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            0,
            CliValueKind.NativeInt,
            WasmTargetLayout.Wasm64);
        var managedAddressLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            0,
            CliValueKind.ManagedAddress,
            WasmTargetLayout.Wasm64);
        // A native pointer and a managed address share the slot's i64 local.
        Assert.Equal(managedAddressLocal, nativeLocal);
        Assert.Equal(WasmOpcodes.LocalGet, instructions[0].Opcode);
        Assert.Equal((uint)nativeLocal, instructions[0].Operand.UnsignedValue);
    }

    [Fact]
    public void DoesNotOwnStaticFieldOperations()
    {
        var emitter = CreateEmitter();
        var request = CreateRequest(CilOperation.LoadStaticField, 0);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Emit(emitter, request));

        Assert.Contains("does not own", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadsAnEntityTypedScalarThroughItsDeclaredStorageType()
    {
        var fieldType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldDefinitionModel(
            InstanceFieldKey,
            TypeKey,
            "Instance",
            fieldType,
            false);
        var layout = new FieldLayout(0)
        {
            Size = 4,
            Type = fieldType,
        };
        var emitter = CreateEmitter(
            new FieldProgram(field, false),
            new FieldLayoutScenario(WasmTargetLayout.Wasm32, layout));
        var request = CreateRequest(
            CilOperation.LoadField,
            [CliValueKind.ManagedReference],
            new CilOperand.Entity(InstanceFieldKey));

        Emit(emitter, request);

        Assert.Equal([CliValueKind.I4], request.Stack);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LoadsPromotedTupleProjectionFromItsScalarLocal(int elementIndex)
    {
        var fieldType = CliTypeIdentity.Primitive("u8", CliValueKind.I8);
        var tuple = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "ValueTuple`2", true),
            [fieldType, fieldType]);
        var field = new FieldDefinitionModel(
            InstanceFieldKey,
            TypeKey,
            elementIndex == 0 ? "Item1" : "Item2",
            fieldType,
            false);
        var plan = new TransientAggregatePromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(7, fieldType),
            ImmutableDictionary<int, TransientAggregateProjection>.Empty.Add(
                0,
                new(7, elementIndex)),
            [],
            []);
        var context = CreateMethodEmissionContext() with
        {
            TransientAggregatePromotions = new(
                plan,
                ImmutableDictionary<int, TransientAggregateScalarLocals>.Empty.Add(
                    7,
                    new(40, 41))),
        };
        var request = CreateRequest(
            CilOperation.LoadField,
            [CliValueKind.ValueType],
            new CilOperand.FieldInstance(new(field, tuple, fieldType)),
            context);

        Emit(CreateEmitter(), request);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        Assert.Equal(CliValueKind.I8, Assert.Single(request.Stack));
        Assert.Equal(WasmOpcodes.LocalGet, instructions[0].Opcode);
        Assert.Equal((uint)(40 + elementIndex), instructions[0].Operand.UnsignedValue);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I64Load or WasmOpcodes.I64Store);
    }

    [Fact]
    public void LoadsAnInstanceValueTypeAtZeroFrameOffsets()
    {
        var valueType = CreateValueType();
        var field = CreateField(valueType);
        var layout = new FieldLayout(0)
        {
            Size = 4,
            Type = valueType,
        };
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                8,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 0),
                []),
        };
        var emitter = CreateEmitter(
            new FieldProgram(field, false),
            new FieldLayoutScenario(WasmTargetLayout.Wasm32, layout));
        var request = CreateRequest(
            CilOperation.LoadField,
            [CliValueKind.ManagedReference],
            new CilOperand.FieldInstance(new FieldInstanceModel(
                field,
                CliTypeIdentity.Named(Assembly, "Test", "Owner", false),
                valueType)),
            context);

        Emit(emitter, request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
    }

    [Fact]
    public void LoadsAnEntityValueTypeWithFrameAndFieldOffsets()
    {
        var valueType = CreateValueType();
        var field = CreateField(valueType);
        var layout = new FieldLayout(8)
        {
            Size = 4,
            Type = valueType,
        };
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                []),
        };
        var emitter = CreateEmitter(
            new FieldProgram(field, true),
            new FieldLayoutScenario(WasmTargetLayout.Wasm32, layout));
        var request = CreateRequest(
            CilOperation.LoadField,
            [CliValueKind.ManagedAddress],
            new CilOperand.Entity(InstanceFieldKey),
            context);

        Emit(emitter, request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
    }

    [Fact]
    public void FieldAddressLoadSupportsZeroOffsetValueReceivers()
    {
        var fieldType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = CreateField(fieldType);
        var emitter = CreateEmitter(
            new FieldProgram(field, true),
            new FieldLayoutScenario(
                WasmTargetLayout.Wasm32,
                new FieldLayout(0) { Size = 4, Type = fieldType }));
        var request = CreateRequest(
            CilOperation.LoadFieldAddress,
            [CliValueKind.ManagedAddress],
            new CilOperand.FieldInstance(new FieldInstanceModel(
                field,
                CliTypeIdentity.Named(Assembly, "Test", "Owner", true),
                fieldType)));

        Emit(emitter, request);

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
    }

    [Fact]
    public void StoresAnEntityTypedScalarThroughItsDeclaredStorageType()
    {
        var fieldType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = CreateField(fieldType);
        var emitter = CreateEmitter(
            new FieldProgram(field, false),
            new FieldLayoutScenario(
                WasmTargetLayout.Wasm32,
                new FieldLayout(0) { Size = 4, Type = fieldType }));
        var request = CreateRequest(
            CilOperation.StoreField,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.Entity(InstanceFieldKey));

        Emit(emitter, request);

        Assert.Empty(request.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StoresAResolvedEnumAsItsScalarStackKind(WasmTarget target)
    {
        var signatureType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "ExternalEnum",
            isValueType: true);
        var resolvedType = signatureType.WithStackStorageType(
            CliTypeIdentity.Primitive("i8", CliValueKind.I8));
        var field = CreateField(signatureType);
        var layouts = new FieldLayoutScenario(
            WasmTargetLayout.For(target),
            new FieldLayout(0) { Size = 8, Type = signatureType });
        var emitter = CreateEmitter(new FieldProgram(field, false), layouts);
        var request = CreateRequest(
            CilOperation.StoreField,
            [CliValueKind.ManagedReference, CliValueKind.I8],
            new CilOperand.FieldInstance(new FieldInstanceModel(
                field,
                CliTypeIdentity.Named(Assembly, "Test", "Owner", false),
                resolvedType)));

        Emit(emitter, request);

        var opcodes = ((RecordingInstructionWriter)GetCodeWriter(request))
            .ToInstructions()
            .Select(instruction => instruction.Opcode)
            .ToArray();
        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.I64Store, opcodes);
        Assert.DoesNotContain(WasmOpcodes.Prefixed, opcodes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void StoresInstanceValueTypesWithEitherFieldOffset(int offset)
    {
        var valueType = CreateValueType();
        var field = CreateField(valueType);
        var emitter = CreateEmitter(
            new FieldProgram(field, false),
            new FieldLayoutScenario(
                WasmTargetLayout.Wasm32,
                new FieldLayout(offset) { Size = 4, Type = valueType }));
        var request = CreateRequest(
            CilOperation.StoreField,
            [CliValueKind.ManagedReference, CliValueKind.ValueType],
            new CilOperand.FieldInstance(new FieldInstanceModel(
                field,
                CliTypeIdentity.Named(Assembly, "Test", "Owner", false),
                valueType)));

        Emit(emitter, request);

        Assert.Empty(request.Stack);
    }

    // A scalar field of a vector value, such as Vector<T>'s ulongs, is the lane
    // its offset names, extracted with the field's width and signedness.
    [Theory]
    [InlineData("f8", 8, 8, WasmSimdOpcodes.F64x2ExtractLane, 1u)]
    [InlineData("u8", 8, 0, WasmSimdOpcodes.I64x2ExtractLane, 0u)]
    [InlineData("f4", 4, 12, WasmSimdOpcodes.F32x4ExtractLane, 3u)]
    [InlineData("i4", 4, 4, WasmSimdOpcodes.I32x4ExtractLane, 1u)]
    [InlineData("i2", 2, 6, WasmSimdOpcodes.I16x8ExtractLaneSigned, 3u)]
    [InlineData("u2", 2, 14, WasmSimdOpcodes.I16x8ExtractLaneUnsigned, 7u)]
    [InlineData("i1", 1, 5, WasmSimdOpcodes.I8x16ExtractLaneSigned, 5u)]
    [InlineData("u1", 1, 15, WasmSimdOpcodes.I8x16ExtractLaneUnsigned, 15u)]
    public void LoadsAScalarFieldOfAVectorValueAsItsLane(string primitive, int size, int offset, uint extract, uint lane)
    {
        var kind = primitive switch
        {
            "f8" => CliValueKind.F8,
            "f4" => CliValueKind.F4,
            "u8" => CliValueKind.I8,
            _ => CliValueKind.I4,
        };
        var fieldType = CliTypeIdentity.Primitive(primitive, kind);
        var emitter = CreateEmitter(
            new FieldProgram(CreateField(fieldType), true),
            new FieldLayoutScenario(WasmTargetLayout.Wasm32, new FieldLayout(offset) { Size = size, Type = fieldType }));
        var request = CreateRequest(CilOperation.LoadField, [CliValueKind.V128], new CilOperand.Entity(InstanceFieldKey));

        Emit(emitter, request);

        Assert.Equal([kind], request.Stack);
        Assert.Contains(((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.SimdPrefixed && instruction.Operand.Alignment == extract &&
            instruction.Operand.Offset == lane);
    }

    // No vector declares a field as wide as itself.
    [Fact]
    public void AScalarFieldSpanningTheWholeVectorIsRejected()
    {
        var fieldType = CliTypeIdentity.Primitive("u8", CliValueKind.I8);
        var emitter = CreateEmitter(
            new FieldProgram(CreateField(fieldType), true),
            new FieldLayoutScenario(WasmTargetLayout.Wasm32, new FieldLayout(0) { Size = 16, Type = fieldType }));
        var request = CreateRequest(CilOperation.LoadField, [CliValueKind.V128], new CilOperand.Entity(InstanceFieldKey));

        var exception = Assert.Throws<CompilerException>(() => Emit(emitter, request));
        Assert.Contains("spans the whole vector", exception.Message, StringComparison.Ordinal);
    }

    // A struct field of a vector, as Plane's Normal, copies out whole 32-bit
    // lanes; every System.Numerics vector is made of them.
    [Theory]
    [InlineData(2, 4)]
    [InlineData(0, 6)]
    public void AVectorsStructFieldOutsideItsLanesIsRejected(int offset, int size)
    {
        var valueType = CreateValueType();
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                8,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 0),
                []),
        };
        var emitter = CreateEmitter(
            new FieldProgram(CreateField(valueType), true),
            new FieldLayoutScenario(WasmTargetLayout.Wasm32, new FieldLayout(offset) { Size = size, Type = valueType }));
        var request = CreateRequest(CilOperation.LoadField, [CliValueKind.V128], new CilOperand.Entity(InstanceFieldKey), context);

        var exception = Assert.Throws<CompilerException>(() => Emit(emitter, request));
        Assert.Contains("not made of 32-bit lanes", exception.Message, StringComparison.Ordinal);
    }

    private static IInstructionCommandProvider CreateEmitter(
        ITypeRepository? types = null,
        IFieldRepository? fieldRepository = null,
        ITargetLayout? layouts = null,
        IInstanceFieldLayoutProvider? fields = null,
        IAddressTakenAggregateProjectionEmitter? projections = null)
    {
        var actualLayouts = layouts ?? new RecordingLayoutProvider();
        var program = new FakeProgram();
        var addresses = new AddressInstructionEmitter(actualLayouts);
        return AsProvider(new FieldInstructionEmitter(
            types ?? program,
            fieldRepository ?? program,
            actualLayouts,
            fields ?? (IInstanceFieldLayoutProvider)actualLayouts,
            new RecordingImplicitExceptionEmitter(),
            addresses,
            projections ?? new AddressTakenAggregateProjectionEmitter(actualLayouts)));
    }

    private static IInstructionCommandProvider CreateEmitter(
        FieldProgram program,
        FieldLayoutScenario layouts) =>
        CreateEmitter(program, program, layouts, layouts);

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        int inputCount) => CreateRequest(
        operation,
        Enumerable.Repeat(
            operation == CilOperation.StoreField
                ? CliValueKind.I4
                : CliValueKind.ManagedReference,
            inputCount)
            .Select((type, index) => operation == CilOperation.StoreField && index == 0
                ? CliValueKind.ManagedReference
                : type)
            .ToArray(),
        new CilOperand.Entity(InstanceFieldKey));

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        IEnumerable<CliValueKind> stack,
        CilOperand operand,
        MethodEmissionContext? context = null)
    {
        return CreateInstructionRequest(
            operation,
            stack,
            operand,
            context);
    }

    private static void Emit(
        IInstructionCommandProvider provider,
        InstructionEmissionRequest request)
    {
        var command = provider.Commands.SingleOrDefault(candidate =>
            candidate.Operation == request.Instruction.Operation) ??
            throw new InvalidOperationException(
                $"{provider.GetType().Name} does not own " +
                $"'{request.Instruction.Operation}'.");
        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());
    }

    private static IInstructionCommandProvider AsProvider(
        IInstructionCommandProvider provider) => provider;

    private static CliTypeIdentity CreateValueType() => CliTypeIdentity.Named(
        Assembly,
        "Test",
        "Value",
        isValueType: true,
        CliValueKind.ValueType);

    private static FieldDefinitionModel CreateField(CliTypeIdentity fieldType) =>
        new(InstanceFieldKey, TypeKey, "Instance", fieldType, false);

    private sealed class FieldProgram(
        FieldDefinitionModel field,
        bool declaringTypeIsValueType) : ITypeRepository, IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => field;

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(
            key,
            "Test",
            "Owner",
            declaringTypeIsValueType,
            [],
            []);
    }

    private sealed class FieldLayoutScenario(
        WasmTargetLayout target,
        FieldLayout layout) : ITargetLayout, IInstanceFieldLayoutProvider
    {
        public WasmTargetLayout Target { get; } = target;

        public FieldLayout GetFieldLayout(FieldInstanceModel field) => layout;

        public FieldLayout GetFieldLayout(EntityKey field) => layout;
    }

    private sealed class RecordingImplicitExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) =>
            Kinds.Add(kind);
    }
}
