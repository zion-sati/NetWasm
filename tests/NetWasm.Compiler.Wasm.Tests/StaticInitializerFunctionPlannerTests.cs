using System.Buffers.Binary;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StaticInitializerFunctionPlannerTests
{
    [Fact]
    public void EmptyGuardSetIsPayAsUsedAndInvalidRequestsFailBeforeMetadataAccess()
    {
        var metadata = new Metadata();
        var planner = Assert.IsAssignableFrom<IStaticInitializerFunctionPlanner>(new StaticInitializerFunctionPlanner(metadata, metadata));
        var request = CreateEmissionRequest();
        var functions = Functions(new([], [], [], []));
        Assert.Throws<ArgumentNullException>(() => planner.Build(null!, functions, ModuleDataPlan.Empty));
        Assert.Throws<ArgumentNullException>(() => planner.Build(request, null!, ModuleDataPlan.Empty));
        Assert.Throws<ArgumentNullException>(() => planner.Build(request, functions, null!));
        Assert.Same(ModuleDataPlan.Empty, planner.Build(request, functions, ModuleDataPlan.Empty));
        Assert.Empty(metadata.Strings);
    }

    [Fact]
    public void DirectAndClosedInitializersKeepFourByteGuardsAndShareCatchMetadataAndFailureConstruction()
    {
        var metadata = new Metadata();
        var planner = Assert.IsAssignableFrom<IStaticInitializerFunctionPlanner>(new StaticInitializerFunctionPlanner(metadata, metadata));
        var request = CreateEmissionRequest();
        var structured = request.Methods[EntryKey];
        var direct = new MethodInstanceModel(metadata.GetMethod(EntryKey),
            CliTypeIdentity.Named(Assembly, "Test", "Owner", false), [], metadata.GetMethod(EntryKey).Signature);
        var closed = new MethodInstanceModel(direct.Definition,
            CliTypeIdentity.GenericInstantiation(direct.DeclaringType, [CliTypeIdentity.FromStackKind(CliValueKind.I4)]), [], direct.Signature);
        request = request with
        {
            Methods = ImmutableDictionary<EntityKey, NetWasm.Compiler.ControlFlow.Structured.StructuredMethod>.Empty.Add(EntryKey,
                structured with { Header = structured.Header with { Method = direct.Definition, MethodInstance = direct } }),
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(closed.CanonicalName, closed),
        };
        var functions = Functions(new(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(EntryKey, new(61)).Add(ConstructorKey, new(62)),
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(closed.CanonicalName, new(63)), [], []));
        var key = StaticInitializerGuard.KeyFor(EntryKey);
        var data = ModuleDataPlan.Empty with
        {
            StaticDataEnd = 249,
            StaticInitializerGuards = ImmutableDictionary<string, StaticInitializerGuard>.Empty
                .Add(key, new(240, EntryKey, null)).Add(closed.CanonicalName, new(244, null, closed.CanonicalName)),
        };

        var result = planner.Build(request, functions, data);

        Assert.Equal(240, result.StaticInitializerGuards[key].Address);
        Assert.Equal(244, result.StaticInitializerGuards[closed.CanonicalName].Address);
        Assert.Equal(100, result.StaticInitializerGuards[key].FunctionIndex.Value);
        Assert.Equal(101, result.StaticInitializerGuards[closed.CanonicalName].FunctionIndex.Value);
        var generated = Assert.IsType<StaticInitializerFunctionPlan>(result.StaticInitializerFunctions);
        Assert.Equal(102, generated.FailureFunctionIndex);
        Assert.Equal([100, 101], generated.Initializers.Select(item => item.FunctionIndex));
        Assert.Equal([61, 63], generated.Initializers.Select(item => item.InitializerIndex));
        Assert.Equal([key, closed.CanonicalName], generated.Initializers.Select(item => item.Key));
        Assert.Empty(metadata.Strings);
        var segment = Assert.Single(result.DataSegments);
        Assert.Equal(252, segment.Address);
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(segment.Data.AsSpan()));
        Assert.Equal(256, result.StaticDataEnd);
        Assert.Null(data.StaticInitializerFunctions);
    }

    private static WasmModulePlan Functions(FunctionIndexMap indices) => new(
        default, [], [], [], StackTraceMethodPlan.Disabled, [], indices,
        new([], default, default, default, default, default, default), default, default, default, default)
    { StaticInitializerFunctionBase = 100 };

    private sealed class Metadata : ITypeDescriptorSource, ITypeRepository, IMethodRepository, IStaticDataLayout
    {
        public List<string> Strings { get; } = [];
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors =>
            [new(TypeKey, 7, 0, 32, 0, 0, null), new(StringTypeKey, 8, 7, 40, 0, 0, null)];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(key, "System",
            key == TypeKey ? "Exception" : "TypeInitializationException", false, [], [EntryKey, ConstructorKey]);
        public MethodDefinitionModel GetMethod(EntityKey key) => new(key, StringTypeKey,
            key == EntryKey ? ".cctor" : ".ctor", key == EntryKey,
            key == EntryKey ? MethodSignatureModel.Create(CliValueKind.Void)
                : MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.ManagedReference, CliValueKind.ManagedReference), 1);
        public StringLayout GetStringLayout(string value)
        {
            Strings.Add(value);
            return new(320 + Strings.Count * 40, value.Length, 8);
        }
        public int StaticDataEnd => 240;
        public ImmutableArray<int> StaticRootAddresses => [];
        public ImmutableArray<DataSegment> DataSegments => [];
    }
}
