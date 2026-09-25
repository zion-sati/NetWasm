using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StaticFieldInstructionEmitterTests
{
    [Fact]
    public void OwnsTheStaticFieldInstructionFamily()
    {
        var emitter = CreateEmitter();

        Assert.Equal(
            [
                CilOperation.LoadStaticField,
                CilOperation.LoadStaticFieldAddress,
                CilOperation.StoreStaticField,
            ],
            emitter.Commands.Select(command => command.Operation));
    }

    [Theory]
    [InlineData(CilOperation.LoadStaticField, CliValueKind.I4)]
    [InlineData(CilOperation.LoadStaticFieldAddress, CliValueKind.ManagedAddress)]
    public void LoadOperationPushesExpectedType(
        CilOperation operation,
        CliValueKind expected)
    {
        var request = CreateRequest(operation, []);

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Equal([expected], request.Stack);
        Assert.NotEmpty(GetCodeBytes(request));
    }

    [Fact]
    public void StoreConsumesValue()
    {
        var request = CreateRequest(
            CilOperation.StoreStaticField,
            [CliValueKind.I4]);

        Emit((IInstructionCommandProvider)CreateEmitter(), request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void ValueLoadCopiesTheStaticValueThroughItsTemporaryFrame(int offset)
    {
        var program = StaticProgram.For(CliValueKind.ValueType);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, offset),
                []),
        };
        var request = CreateRequest(
            program,
            CilOperation.LoadStaticField,
            [],
            new CilOperand.FieldInstance(program.FieldInstance),
            context);

        Emit((IInstructionCommandProvider)CreateEmitter(program), request);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void ValueStoreCopiesTheTemporaryValueToStaticMemory()
    {
        var program = StaticProgram.For(CliValueKind.ValueType);
        var request = CreateRequest(
            program,
            CilOperation.StoreStaticField,
            [CliValueKind.ValueType],
            new CilOperand.FieldInstance(program.FieldInstance));

        Emit((IInstructionCommandProvider)CreateEmitter(program), request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void DirectStaticInitializerGuardCallsTheDirectConstructor()
    {
        var program = StaticProgram.For(CliValueKind.I4, hasInitializer: true);
        var guardKey = StaticInitializerGuard.KeyFor(program.InitializerKey);
        var target = CreateInstructionModuleTarget() with
        {
            ModuleData = ModuleDataPlan.Empty with
            {
                StaticInitializerGuards = ImmutableDictionary<string, StaticInitializerGuard>.Empty.Add(
                    guardKey,
                    new(240, program.InitializerKey, null) { FunctionIndex = OptionalFunctionIndex.At(41) }),
            },
        };
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                program.InitializerKey,
                new(41)),
            [],
            [],
            []);
        var request = CreateRequest(
            program,
            CilOperation.LoadStaticField,
            [],
            new CilOperand.Entity(program.FieldKey),
            target: target);

        Emit(
            (IInstructionCommandProvider)CreateEmitter(program),
            request,
            new FunctionIndexResolver(program, program, indices));

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void ConstructedStaticInitializerGuardCallsTheConstructedConstructor()
    {
        var program = StaticProgram.For(CliValueKind.I4, hasInitializer: true);
        var instance = program.ConstructedFieldInstance;
        var guardKey = $"{instance.DeclaringType.CanonicalName}::0x{program.InitializerKey.MetadataToken:x8}";
        var target = CreateInstructionModuleTarget() with
        {
            ModuleData = ModuleDataPlan.Empty with
            {
                StaticInitializerGuards = ImmutableDictionary<string, StaticInitializerGuard>.Empty.Add(
                    guardKey,
                    new(244, null, guardKey) { FunctionIndex = OptionalFunctionIndex.At(42) }),
            },
        };
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty,
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(guardKey, new(42)),
            [],
            []);
        var request = CreateRequest(
            program,
            CilOperation.LoadStaticFieldAddress,
            [],
            new CilOperand.FieldInstance(instance),
            target: target);

        Emit(
            (IInstructionCommandProvider)CreateEmitter(program),
            request,
            new FunctionIndexResolver(program, program, indices));

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void MissingStaticInitializerGuardDoesNotEmitGuardInstructions()
    {
        var program = StaticProgram.For(CliValueKind.I4, hasInitializer: true);
        var request = CreateRequest(
            program,
            CilOperation.LoadStaticField,
            [],
            new CilOperand.Entity(program.FieldKey));

        Emit((IInstructionCommandProvider)CreateEmitter(program), request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.DoesNotContain(WasmOpcodes.If, GetCodeBytes(request));
    }

    private static void Emit<TProvider>(
        TProvider provider,
        InstructionEmissionRequest request,
        IFunctionIndexResolver? functionIndices = null)
        where TProvider : IInstructionCommandProvider
    {
        var command = provider.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(
            request,
            GetCodeWriter(request),
            functionIndices ?? CreateFunctionIndexResolver());
    }

    private static StaticFieldInstructionEmitter CreateEmitter(
        StaticProgram? program = null)
    {
        var layouts = new RecordingLayoutProvider();
        var actualProgram = program ?? StaticProgram.For(CliValueKind.I4);
        return new(actualProgram, layouts, layouts, layouts,
            new AddressInstructionEmitter(layouts),
            new StaticInitializationEmitter(actualProgram, actualProgram, new AddressInstructionEmitter(layouts)));
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        IEnumerable<CliValueKind> stack) =>
        CreateInstructionRequest(
            operation,
            stack,
            new CilOperand.Entity(StaticFieldKey));

    private static InstructionEmissionRequest CreateRequest(
        StaticProgram program,
        CilOperation operation,
        IEnumerable<CliValueKind> stack,
        CilOperand operand,
        MethodEmissionContext? context = null,
        InstructionModuleTarget? target = null)
    {
        var request = new InstructionEmissionRequest(
            Header(new CilMethodBody(program.Entry, 3, [], [])),
            I(0, operation, operand),
            [.. stack],
            context ?? CreateMethodEmissionContext(),
            target ?? CreateInstructionModuleTarget());
        RegisterInstructionWriter(request, new RecordingInstructionWriter());
        return request;
    }

    private sealed class StaticProgram :
        ITypeRepository,
        IFieldRepository,
        IMethodRepository,
        ISymbolFormatter
    {
        private readonly TypeDefinitionModel _type;
        private readonly FieldDefinitionModel _field;
        private readonly MethodDefinitionModel _initializer;
        private readonly ImmutableDictionary<EntityKey, MethodDefinitionModel> _methods;

        private StaticProgram(CliTypeIdentity fieldType, bool hasInitializer)
        {
            var typeKey = Key(0x02000010);
            FieldKey = Key(0x04000010);
            InitializerKey = Key(0x06000010);
            var methods = hasInitializer
                ? ImmutableArray.Create(InitializerKey)
                : ImmutableArray<EntityKey>.Empty;
            _type = new(typeKey, "Test", "StaticOwner", false, [FieldKey], methods);
            _field = new(FieldKey, typeKey, "Value", fieldType, true);
            _initializer = new(
                InitializerKey,
                typeKey,
                ".cctor",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            _methods = ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty.Add(
                InitializerKey,
                _initializer);
            Entry = new(
                EntryKey,
                typeKey,
                "Run",
                true,
                MethodSignatureModel.Create(CliValueKind.I4),
                1);
            FieldInstance = new(
                _field,
                CliTypeIdentity.Named(Assembly, "Test", "StaticOwner", isValueType: false),
                fieldType);
            ConstructedFieldInstance = new(
                _field,
                CliTypeIdentity.GenericInstantiation(
                    CliTypeIdentity.Named(Assembly, "Test", "StaticOwner`1", isValueType: false),
                    [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]),
                fieldType);
        }

        public EntityKey FieldKey { get; }
        public EntityKey InitializerKey { get; }
        public MethodDefinitionModel Entry { get; }
        public FieldInstanceModel FieldInstance { get; }
        public FieldInstanceModel ConstructedFieldInstance { get; }

        public static StaticProgram For(
            CliValueKind fieldType,
            bool hasInitializer = false) => new(
            CliTypeIdentity.FromStackKind(fieldType),
            hasInitializer);

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => _type;
        public FieldDefinitionModel GetField(EntityKey key) => _field;
        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
        public string Format(EntityKey key) => _type.FullName;
        public string Format(MethodDefinitionModel method) =>
            $"{_type.FullName}::{method.Name}";
    }
}
