using System.Collections.Immutable;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.Tests.EmitterTestSupport;

using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

namespace NetWasm.Compiler.Wasm.Tests;

using StructuredMethod = NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;

internal static class EmitterTestSupport
{
    private static readonly ConditionalWeakTable<
        InstructionEmissionRequest,
        RecordingInstructionWriter> Writers = [];

    internal sealed class RecordingInstructionWriter : IWasmInstructionWriter
    {
        private readonly List<WasmInstruction> _instructions = [];

        public void Write(WasmInstruction instruction)
        {
            ArgumentNullException.ThrowIfNull(instruction);
            _instructions.Add(instruction);
        }

        public byte[] ToArray()
        {
            var buffer = new WasmBinaryBuffer();
            var binary = new WasmBinaryWriter(buffer);
            var writer = new WasmInstructionWriter(binary);
            foreach (var instruction in _instructions)
            {
                writer.Write(instruction);
            }
            return new WasmBinarySnapshotReader(buffer).Read();
        }

        public ImmutableArray<WasmInstruction> ToInstructions() =>
            [.. _instructions];
    }

    internal static byte[] GetCodeBytes(InstructionEmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Writers.TryGetValue(request, out var writer)
            ? writer.ToArray()
            : throw new InvalidOperationException(
                "Instruction test requests must use the recording instruction writer.");
    }

    internal static IWasmInstructionWriter GetCodeWriter(
        InstructionEmissionRequest request) =>
        Writers.TryGetValue(request, out var writer)
            ? writer
            : throw new InvalidOperationException(
                "Instruction test requests must use the recording instruction writer.");

    internal static void RegisterInstructionWriter(
        InstructionEmissionRequest request,
        RecordingInstructionWriter writer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);
        Writers.Add(request, writer);
    }

    internal static void Emit(
        this InstructionCommandProvider provider,
        InstructionEmissionRequest request,
        IFunctionIndexResolver? functionIndices = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        var command = provider.Commands.SingleOrDefault(candidate =>
            candidate.Operation == request.Instruction.Operation) ??
            throw new InvalidOperationException(
                $"{provider.GetType().Name} does not own " +
                $"'{request.Instruction.Operation}'.");
        command.Emit(
            request,
            Writers.TryGetValue(request, out var writer)
                ? writer
                : throw new InvalidOperationException(
                    "Instruction test requests must use the recording instruction writer."),
            functionIndices ?? CreateFunctionIndexResolver());
    }

    internal static void Emit(
        this InstructionCommandProvider provider,
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        IFunctionIndexResolver? functionIndices = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        var command = provider.Commands.SingleOrDefault(candidate =>
            candidate.Operation == request.Instruction.Operation) ??
            throw new InvalidOperationException(
                $"{provider.GetType().Name} does not own " +
                $"'{request.Instruction.Operation}'.");
        command.Emit(request, code, functionIndices ?? CreateFunctionIndexResolver());
    }

    public static ManagedWasmEmitter CreateManagedWasmEmitter<TLayouts>(
        FakeProgram program,
        IRuntimeIntrinsicRegistry intrinsics,
        TLayouts layouts,
        IWasmModuleEmitterFactory modules)
        where TLayouts : ITargetLayout, IValueLayoutProvider, ITypeLayoutProvider,
            IInstanceFieldLayoutProvider, IStaticFieldLayoutProvider,
            IStaticDataLayout, IRuntimeObjectLayout, IRectangularArrayLayoutProvider,
            IManagedExceptionObjectProvider,
            ITypeDescriptorSource => new(
        program,
        program,
        program,
        program,
        program,
        program,
        intrinsics,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        modules);

    public static IServiceCollection AddWasmModuleEmission<TLayouts>(
        IServiceCollection services,
        FakeProgram program,
        IRuntimeIntrinsicRegistry intrinsics,
        TLayouts layouts)
        where TLayouts : ITargetLayout, IValueLayoutProvider, ITypeLayoutProvider,
            IInstanceFieldLayoutProvider, IStaticFieldLayoutProvider,
            IStaticDataLayout, IRuntimeObjectLayout, IRectangularArrayLayoutProvider,
            IManagedExceptionObjectProvider,
        ITypeDescriptorSource => services.AddWasmModuleEmission(
        program,
        program,
        program,
        program,
        program,
        program,
        intrinsics,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts);

    public static WasmModuleEmissionResult EmitModule<TLayouts>(
        IWasmModuleEmitterFactory factory,
        FakeProgram program,
        IRuntimeIntrinsicRegistry intrinsics,
        TLayouts layouts,
        WasmEmissionRequest request)
        where TLayouts : ITargetLayout, IValueLayoutProvider, ITypeLayoutProvider,
            IInstanceFieldLayoutProvider, IStaticFieldLayoutProvider,
            IStaticDataLayout, IRuntimeObjectLayout, IRectangularArrayLayoutProvider,
            IManagedExceptionObjectProvider,
        ITypeDescriptorSource => factory.Emit(
        program,
        program,
        program,
        program,
        program,
        program,
        intrinsics,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        layouts,
        request);

    public static IManagedBoundaryFailureDispositionResolver
        CreateBoundaryFailureDispositions() => new ManagedBoundaryFailurePolicy();

    public static IJavaScriptResultDescriptorSizeResolver CreateResultDescriptorSizes(
        RecordingLayoutProvider layouts) =>
        new JavaScriptResultDescriptorSizeResolver(layouts);

    public static IJavaScriptImportParameterTypeResolver CreateImportParameterTypes(
        RecordingLayoutProvider layouts) =>
        new JavaScriptImportParameterTypeResolver(layouts);

    public static ICilTypeIdentityResolver CreateTypeIdentities(FakeProgram program) =>
        new CilTypeIdentityResolver(program);

    public static ICilTypeOperandResolver CreateTypeOperands(FakeProgram program) =>
        new CilTypeOperandResolver(CreateTypeIdentities(program));

    public static IManagedCallSiteResolver CreateMethodOperands(FakeProgram program) =>
        new TestManagedCallSiteResolver(program, CreateTypeIdentities(program));

    public static IArgumentTypeResolver CreateArgumentTypes(FakeProgram program) =>
        new ArgumentTypeResolver(program);

    public static IArgumentSignatureTypeResolver CreateArgumentSignatureTypes(
        FakeProgram program) =>
        new ArgumentSignatureTypeResolver(CreateTypeIdentities(program));

    public static ValueFrameLayoutPlanner CreateValueFrameLayoutPlanner(
        FakeProgram program,
        RecordingLayoutProvider layouts) => new ValueFrameLayoutPlanner(
        program,
        program,
        layouts,
        layouts,
        CreateTypeOperands(program),
        CreateTypeIdentities(program),
        CreateArgumentTypes(program),
        CreateArgumentSignatureTypes(program));

    public static IValueFrameAddressEmitter CreateValueFrameAddresses(
        RecordingLayoutProvider layouts) => new ValueFrameAddressEmitter(layouts);

    public static IFilterEnvironmentRootEmitter CreateFilterEnvironmentRoots(
        RecordingLayoutProvider layouts) => new FilterEnvironmentRootEmitter(
        layouts,
        CreateValueFrameAddresses(layouts),
        CreateAddressInstructions(layouts));

    public static IMethodFrameEntryEmitter CreateMethodFrameEntry(
        FakeProgram program,
        RecordingLayoutProvider layouts,
        IRuntimeImportResolver runtimeImports) => new MethodFrameEntryEmitter(
        layouts,
            layouts,
            CreateArgumentSignatureTypes(program),
            runtimeImports,
            new StackTraceFrameEntryEmitter(runtimeImports),
            CreateValueFrameAddresses(layouts),
        CreateFilterEnvironmentRoots(layouts));

    public static IMethodFrameExitEmitter CreateMethodFrameExit(
        IRuntimeImportResolver runtimeImports) =>
            new MethodFrameExitEmitter(
                runtimeImports,
                new StackTraceFrameExitEmitter(runtimeImports));

    public static IAddressInstructionEmitter CreateAddressInstructions(
        RecordingLayoutProvider layouts) => new AddressInstructionEmitter(layouts);

    public static IOptionalFunctionIndexValidator CreateOptionalFunctionIndexValidator() =>
        new OptionalFunctionIndexValidator();

    public static IRuntimeCoreInitializer CreateRuntimeCoreInitializer(
        RecordingLayoutProvider layouts,
        IRuntimeImportResolver runtimeImports) => new RuntimeCoreInitializer(
            layouts,
            layouts,
            runtimeImports,
            CreateAddressInstructions(layouts));

    public static IRuntimeStateInitializer CreateRuntimeStateInitializer(
        RecordingLayoutProvider layouts,
        IRuntimeImportResolver runtimeImports)
    {
        var core = CreateRuntimeCoreInitializer(layouts, runtimeImports);
        return new RuntimeStateInitializer(
            layouts,
            layouts,
            runtimeImports,
            CreateAddressInstructions(layouts),
            core);
    }

    public static IReferenceComparisonEmitter CreateReferenceComparisons(
        RecordingLayoutProvider layouts) => new ReferenceComparisonEmitter(layouts);

    public static IDelegateLeafEqualityEmitter CreateDelegateLeafEquality(
        RecordingLayoutProvider layouts) => new DelegateLeafEqualityEmitter(
        layouts,
        layouts,
        CreateReferenceComparisons(layouts));

    internal static readonly AssemblyIdentity Assembly = new("Test");
    internal static readonly EntityKey TypeKey = Key(0x02000001);
    internal static readonly EntityKey StringTypeKey = Key(0x02000002);
    internal static readonly EntityKey EntryKey = Key(0x06000001);
    internal static readonly EntityKey ConstructorKey = Key(0x06000002);
    internal static readonly EntityKey StringMethodKey = Key(0x06000003);
    internal static readonly EntityKey StringLengthKey = Key(0x06000004);
    internal static readonly EntityKey InstanceFieldKey = Key(0x04000001);
    internal static readonly EntityKey StaticFieldKey = Key(0x04000002);

    internal static StructuredMethodHeader Header(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new StructuredMethodHeader(
            body.Method,
            body.MethodInstance,
            body.MaxStack,
            body.Locals,
            body.LocalSignatureTypes,
            body.Instructions);
    }
    internal static ImmutableDictionary<EntityKey, MethodRootMap> EmptyRootMaps(
        IEnumerable<EntityKey> methods) => methods.ToImmutableDictionary(
        method => method,
        method => new MethodRootMap(
            method,
            [],
            []));

    internal static StructuredMethod Structure(
        FakeProgram program,
        MethodDefinitionModel method,
        params CilInstruction[] instructions)
        => StructureWithLocals(program, method, [], instructions);

    internal static StructuredMethod StructureWithLocals(
        FakeProgram program,
        MethodDefinitionModel method,
        ImmutableArray<CliValueKind> locals,
        params CilInstruction[] instructions)
    {
        var body = new CilMethodBody(method, 3, locals, [.. instructions]);
        var graph =
            ((NetWasm.Compiler.ControlFlow.IControlFlowGraphBuilderFactory)
                new NetWasm.Compiler.ControlFlow.ControlFlowGraphBuilderFactory())
                .Create()
                .Build(body);
        var validated = new NetWasm.Compiler.ControlFlow.TypedStackValidator(
            program,
            program,
            program,
            new NetWasm.Compiler.ControlFlow.StackTypeCompatibilityValidator()).Validate(graph);
        return new ValidatedStructuredMethodBuilderFactory()
            .Create()
            .Build(validated);
    }

    internal static StructuredMethod Structure(FakeProgram program, CilMethodBody body)
    {
        var graph =
            ((NetWasm.Compiler.ControlFlow.IControlFlowGraphBuilderFactory)
                new NetWasm.Compiler.ControlFlow.ControlFlowGraphBuilderFactory())
                .Create()
                .Build(body);
        var validated = new NetWasm.Compiler.ControlFlow.TypedStackValidator(
            program,
            program,
            program,
            new NetWasm.Compiler.ControlFlow.StackTypeCompatibilityValidator()).Validate(graph);
        return new ValidatedStructuredMethodBuilderFactory()
            .Create()
            .Build(validated);
    }

    internal static StructuredMethod WithExceptionGroups(
        StructuredMethod method,
        params StructuredExceptionGroup[] groups) => method with
        {
            TopLevelExceptionGroups = [.. groups.Select(group => group.Id)],
            ExceptionGroups = groups.ToImmutableDictionary(group => group.Id),
        };

    internal static StructuredExceptionGroup ExceptionGroup(
        StructuredMethod method,
        int id,
        params StructuredExceptionClause[] clauses) => new(
        new StructuredExceptionGroupId(id),
        null,
        0,
        0,
        [],
        [.. clauses],
        [],
        null,
        null)
        {
            ProtectedBlocks = [],
        };

    internal static StructuredExceptionClause ExceptionClause(
        StructuredMethod method,
        CilExceptionRegionKind kind,
        bool hasFilterBody) =>
        new(
            kind,
            0,
            1,
            null,
            hasFilterBody ? 0 : null,
            StructuredSequence.Empty,
            hasFilterBody ? EntrySequence(method) : null)
        {
            HandlerBlock = method.EntryBlock,
            FilterBlock = hasFilterBody ? method.EntryBlock : null,
        };

    internal static StructuredSequence EntrySequence(StructuredMethod method) =>
        new([new StructuredCode(new StructuredBlockOccurrence(
            method.EntryBlock,
            StructuredBlockRole.Owner))]);

    internal static CilInstruction I(
        int offset,
        CilOperation operation,
        CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    internal static ImmutableArray<InstructionCommand> CreateInstructionCommands(
        Action<InstructionEmissionRequest> emit,
        IEnumerable<CilOperation>? operationsOwnedElsewhere = null)
    {
        var excluded = operationsOwnedElsewhere?.ToImmutableHashSet() ?? [];
        var families = InstructionFamilyCatalogFactory.CreateDefault();
        return
        [
            .. SupportedCil.Operations
                .Where(operation => !excluded.Contains(operation))
                .Select(operation => new InstructionCommand(
                    operation,
                    families.Resolve(operation),
                    (request, _) => emit(request))),
        ];
    }

    internal static EntityKey Key(int token) => new(Assembly, token);

    internal static MethodEmissionContext CreateMethodEmissionContext(
        int maxStack = 3)
    {
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        return new MethodEmissionContext(
            roots,
            0,
            0,
            WasmLocalLayoutPlanner.CreateEvaluationStack(0, maxStack),
            18,
            19,
            20,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            21,
            new ValueFrameLayout(
                0,
                [],
                [],
                [],
                []),
            22,
            FilterEnvironmentLayout.Empty,
            0,
            23,
            24,
            25,
            26,
            27,
            28);
    }

    internal static InstructionEmissionRequest CreateInstructionRequest(
        CilOperation operation,
        IEnumerable<CliValueKind>? stack = null,
        CilOperand? operand = null,
        MethodEmissionContext? context = null,
        int maxStack = 3)
    {
        var program = new FakeProgram();
        var writer = new RecordingInstructionWriter();
        var request = new InstructionEmissionRequest(
            Header(new CilMethodBody(program.GetMethod(EntryKey), maxStack, [], [])),
            I(0, operation, operand),
            stack is null ? [] : [.. stack],
            context ?? CreateMethodEmissionContext(maxStack),
            CreateInstructionModuleTarget(program));
        Writers.Add(request, writer);
        return request;
    }

    internal static InstructionModuleTarget CreateInstructionModuleTarget(
        FakeProgram? program = null)
    {
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                .Add(EntryKey, new(30))
                .Add(ConstructorKey, new(31)),
            [],
            [],
            []);
        return new(
            indices,
            new InteropImportPlan([], default, default, default, default, default, default),
            ModuleDataPlan.Empty,
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty,
            ImmutableDictionary<string, TypeTestSiteModel>.Empty,
            ImmutableDictionary<string, MethodInstanceModel>.Empty,
            [CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false)],
            [],
            ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty,
            StackTraceMethodPlan.Disabled,
            new RuntimeImportSelection(
                WasmModuleProfile.CoreApplication,
                IncludeTerminalExceptionReporter: true),
            OptionalFunctionIndex.At(41),
            OptionalFunctionIndex.At(42),
            ManagedCallSites: []);
    }

    internal static IFunctionIndexResolver CreateFunctionIndexResolver(
        FakeProgram? program = null,
        FunctionIndexMap? indices = null)
    {
        var actualProgram = program ?? new FakeProgram();
        return new FunctionIndexResolver(
            actualProgram,
            actualProgram,
            indices ?? CreateInstructionModuleTarget(actualProgram).FunctionIndices);
    }

    internal static WasmEmissionRequest CreateEmissionRequest(FakeProgram? program = null)
    {
        var actualProgram = program ?? new FakeProgram();
        var entry = actualProgram.GetMethod(EntryKey);
        var methods = ImmutableDictionary<EntityKey, StructuredMethod>.Empty.Add(
            EntryKey,
            Structure(
                actualProgram,
                entry,
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(1, CilOperation.Return)));
        return WasmEmissionRequest.Create(
            entry,
            methods,
            EmptyRootMaps(methods.Keys),
            [],
            ImmutableDictionary<string, EntityKey>.Empty);
    }

    internal static RuntimeIntrinsicEmissionRequest CreateRuntimeIntrinsicRequest(
        RuntimeIntrinsic intrinsic,
        IEnumerable<CliValueKind> stack,
        ImmutableArray<CliTypeIdentity> methodArguments = default,
        CliTypeIdentity? constrainedType = null)
    {
        var program = new FakeProgram();
        var values = stack.ToArray();
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            values,
            maxStack: Math.Max(values.Length, 1));
        var method = program.GetMethod(EntryKey);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            methodArguments.IsDefault ? [] : methodArguments,
            method.Signature);
        return new(
            new CallEmissionRequest(instruction, instance, 0, values.Length),
            intrinsic,
            constrainedType,
            WasmTargetLayout.Wasm32,
            CreateFunctionIndexResolver(program));
    }

    internal static DelegateHelperTarget CreateDelegateHelperTarget() => new(
        [CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false)],
        new DelegateTraversalFunctionIndices(
            OptionalFunctionIndex.At(40),
            OptionalFunctionIndex.At(41)));

    internal static IJavaScriptImportResultEmitter CreateJavaScriptImportResults(
        RecordingLayoutProvider layouts)
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var exceptions = new ImplicitExceptionEmitter(layouts, layouts, 7);
        var addresses = CreateAddressInstructions(layouts);
        var functionIndices = new OptionalFunctionIndexValidator();
        var resultHandles = new JavaScriptResultHandleCapturer(runtimeImports);
        var stackLocals = new StackLocalResolver(layouts);
        var interopHandles = new InteropHandleReleaser(runtimeImports);
        var resultLengths = new JavaScriptResultLengthValidator(
            interopHandles,
            exceptions);
        var allocations = new AllocationResultValidator(
            addresses,
            exceptions);
        var emitters = new JavaScriptImportResultEmitterRegistry(
        [
            new(JavaScriptImportResultKind.String,
                new StringJavaScriptImportResultMarshaller(
                layouts,
                runtimeImports,
                exceptions,
                functionIndices,
                resultHandles,
                stackLocals,
                resultLengths,
                interopHandles,
                allocations,
                addresses)),
            new(JavaScriptImportResultKind.ByteArray,
                new ByteArrayJavaScriptImportResultMarshaller(layouts, layouts, runtimeImports,
                exceptions,
                functionIndices,
                resultHandles,
                stackLocals,
                resultLengths,
                interopHandles,
                allocations,
                addresses)),
            new(JavaScriptImportResultKind.HostObject,
                new HostObjectJavaScriptImportResultMarshaller(
                    layouts,
                    layouts,
                    runtimeImports,
                    functionIndices,
                    resultHandles,
                    stackLocals,
                    interopHandles,
                    allocations,
                    addresses)),
            new(JavaScriptImportResultKind.Scalar,
                new ScalarJavaScriptImportResultMarshaller(
                    layouts,
                    runtimeImports,
                    stackLocals)),
        ]);
        return new JavaScriptImportResultMarshaller(
            new JavaScriptImportResultKindResolver(),
            emitters);
    }

}

internal sealed class RecordingRootPublicationEmitter(
    Action<InstructionEmissionRequest, IWasmInstructionWriter, bool> emit) :
    IRootPublicationEmitter
{
    public RecordingRootPublicationEmitter(Action<InstructionEmissionRequest, bool> emit) :
        this((request, _, constructorCall) => emit(request, constructorCall))
    {
    }

    public RecordingRootPublicationEmitter(Action<InstructionEmissionRequest> emit) :
        this((request, _, _) => emit(request))
    {
    }

    public void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        bool constructorCall = false) => emit(request, code, constructorCall);
}

internal sealed class FakeIntrinsics : IRuntimeIntrinsicRegistry
{
    public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
    {
        intrinsic = RuntimeIntrinsic.StringLength;
        return method == StringLengthKey;
    }
}

internal sealed class EmptyEnumStorageResolver : IEnumStorageResolver
{
    public ImmutableArray<EnumStorage> Resolve() => [];
}

internal sealed class RecordingLayoutProvider(
    WasmTargetLayout? target = null) :
    ITargetLayout,
    IValueLayoutProvider,
    ITypeLayoutProvider,
    IInstanceFieldLayoutProvider,
    IStaticFieldLayoutProvider,
    IStaticDataLayout,
    IRuntimeObjectLayout,
    IRectangularArrayLayoutProvider,
    IManagedExceptionObjectProvider,
    ITypeDescriptorSource,
    IEnumMetadataSource
{
    public EntityKey? ObjectRequest { get; private set; }
    public CliTypeIdentity? ObjectIdentityRequest { get; private set; }
    public EntityKey? FieldRequest { get; private set; }
    public EntityKey? StaticFieldRequest { get; private set; }
    public string? StringRequest { get; private set; }

    public WasmTargetLayout Target { get; } = target ?? WasmTargetLayout.Wasm32;
    public int StringLengthOffset => 44;
    public int StringDataOffset => 48;
    public int ArrayLengthOffset => Target.ObjectHeaderSize;
    public int ArrayDataPointerOffset => WasmTargetLayout.Align(
        ArrayLengthOffset + WasmTargetLayout.SemanticLengthSize,
        Target.AddressSize);
    public int ArrayElementTypeIdOffset => ArrayDataPointerOffset + Target.AddressSize;
    public RectangularArrayLayout Provide() =>
        RectangularArrayLayout.Create(Target, ArrayElementTypeIdOffset);
    public int ReferenceArrayTypeId => 12;
    public int StringTypeId => 13;
    public int TypeTypeId => 14;
    public int DelegateTargetOffset => 4;
    public int DelegateMethodIdOffset => 8;
    public int DelegateLeftOffset => 12;
    public int DelegateRightOffset => 16;
    public int GetExceptionObject(ManagedExceptionKind kind) => 300 + (int)kind * 4;
    public int StaticDataEnd => 256;
    public ImmutableArray<TypeDescriptorLayout> TypeDescriptors =>
        [new(TypeKey, 9, 0, 16, 128, 4, null)];
    public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];
    public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
    public ImmutableArray<EnumMetadataLayout> EnumMetadata => [];
    public ImmutableArray<int> StaticRootAddresses => [200];
    public ImmutableArray<DataSegment> DataSegments => [];

    public ObjectLayout GetObjectLayout(EntityKey type)
    {
        ObjectRequest = type;
        return new ObjectLayout(9, 16, []);
    }

    public ObjectLayout GetObjectLayout(CliTypeIdentity type)
    {
        ObjectIdentityRequest = type;
        return new ObjectLayout(9, 16, []);
    }

    public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
    {
        layout = GetObjectLayout(type);
        return true;
    }

    public ValueLayout GetValueLayout(CliTypeIdentity type) =>
        new(type, 4, 4, type.StackKind == CliValueKind.ManagedReference ? [0] : []);

    public FieldLayout GetFieldLayout(EntityKey field)
    {
        FieldRequest = field;
        return new FieldLayout(52);
    }

    public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
        GetFieldLayout(field.Definition.Key);

    public StaticFieldLayout GetStaticFieldLayout(EntityKey field)
    {
        StaticFieldRequest = field;
        return new StaticFieldLayout(200);
    }

    public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) =>
        GetStaticFieldLayout(field.Definition.Key);

    public StringLayout GetStringLayout(string value)
    {
        StringRequest = value;
        return new StringLayout(220, 1, StringDataOffset);
    }
}

internal sealed class FakeProgram :
    ITypeRepository,
    ITypeDefinitionResolver,
    IFieldRepository,
    IMethodRepository,
    ISymbolFormatter,
    ITypeClassifier
{
    public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
        GetTypeDefinition(identity.FullName == "System.String"
            ? StringTypeKey
            : TypeKey);

    public bool IsDelegateType(EntityKey type) => false;

    private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods = new()
    {
        [EntryKey] = Method(EntryKey, TypeKey, "Run", true, CliValueKind.I4, CliValueKind.I4),
        [ConstructorKey] = Method(ConstructorKey, TypeKey, ".ctor", false, CliValueKind.Void),
        [StringMethodKey] = Method(
            StringMethodKey,
            TypeKey,
            "StringValue",
            true,
            CliValueKind.I4,
            CliValueKind.I4),
        [StringLengthKey] = Method(
            StringLengthKey,
            StringTypeKey,
            "get_Length",
            false,
            CliValueKind.I4),
    };

    public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(
        key,
        "Test",
        key == StringTypeKey ? "String" : "Type",
        false,
        [InstanceFieldKey, StaticFieldKey],
        [.. _methods.Keys]);

    public FieldDefinitionModel GetField(EntityKey key) => key == StaticFieldKey
        ? new(key, TypeKey, "Static", CliValueKind.I4, true)
        : new(key, TypeKey, "Instance", CliValueKind.I4, false);

    public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];

    public string Format(EntityKey key) =>
        key == StringTypeKey ? "System.String" : "Test.Type";

    public string Format(MethodDefinitionModel method) =>
        Format(method.DeclaringType) + "::" + method.Name;

    private static MethodDefinitionModel Method(
        EntityKey key,
        EntityKey declaringType,
        string name,
        bool isStatic,
        CliValueKind result,
        params CliValueKind[] parameters) => new(
            key,
            declaringType,
            name,
            isStatic,
            MethodSignatureModel.Create(result, parameters),
            1);
}
