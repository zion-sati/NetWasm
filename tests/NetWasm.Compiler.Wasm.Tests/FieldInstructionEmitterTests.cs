using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FieldInstructionEmitterTests
{
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
        Assert.NotEqual(managedAddressLocal, nativeLocal);
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

    private static IInstructionCommandProvider CreateEmitter(
        ITypeRepository? types = null,
        IFieldRepository? fieldRepository = null,
        ITargetLayout? layouts = null,
        IInstanceFieldLayoutProvider? fields = null)
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
            addresses));
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

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
