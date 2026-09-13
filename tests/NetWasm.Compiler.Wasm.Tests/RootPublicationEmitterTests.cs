using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class RootPublicationEmitterTests
{
    [Fact]
    public void PublishesNormalAndConstructorRootSets()
    {
        var argument = new RootSource(RootSourceKind.Argument, 0);
        var allocation = new RootSource(RootSourceKind.AllocationTemporary, 0);
        var rootMap = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty
                .Add(argument, 0)
                .Add(allocation, 1),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [argument], [argument, allocation])));
        var context = CreateMethodEmissionContext() with { RootMap = rootMap };
        var normal = CreateInstructionRequest(
            CilOperation.Call,
            context: context);
        var constructor = CreateInstructionRequest(
            CilOperation.NewObject,
            context: context);
        var emitter = CreateEmitter();

        EmitThroughCapability(emitter, normal, GetCodeWriter(normal));
        EmitThroughCapability(
            emitter,
            constructor,
            GetCodeWriter(constructor),
            constructorCall: true);

        Assert.True(EmitterTestSupport.GetCodeBytes(normal).Length > 0);
        Assert.True(EmitterTestSupport.GetCodeBytes(constructor).Length > EmitterTestSupport.GetCodeBytes(normal).Length);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant, WasmOpcodes.I32Add)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant, WasmOpcodes.I64Add)]
    public void PublishesEveryRootSourceKindForEachAddressWidth(
        WasmTarget target,
        byte expectedConstant,
        byte expectedAdd)
    {
        var argument = new RootSource(RootSourceKind.Argument, 0);
        var local = new RootSource(RootSourceKind.Local, 0);
        var spilledLocal = new RootSource(RootSourceKind.Local, 1);
        var evaluationStack = new RootSource(RootSourceKind.EvaluationStack, 0, 4);
        var directEvaluationStack = new RootSource(RootSourceKind.EvaluationStack, 1);
        var addressArgument = new RootSource(RootSourceKind.ManagedAddressArgument, 1);
        var addressLocal = new RootSource(RootSourceKind.ManagedAddressLocal, 1);
        var addressEvaluationStack = new RootSource(
            RootSourceKind.ManagedAddressEvaluationStack,
            1);
        var allocation = new RootSource(RootSourceKind.AllocationTemporary, 0);
        var sources = ImmutableArray.Create(
            argument,
            local,
            spilledLocal,
            evaluationStack,
            directEvaluationStack,
            addressArgument,
            addressLocal,
            addressEvaluationStack,
            allocation);
        var rootMap = new MethodRootMap(
            EntryKey,
            sources.Select((source, index) => (source, index)).ToImmutableDictionary(
                pair => pair.source,
                pair => pair.index),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, sources, [])));
        var context = CreateMethodEmissionContext() with
        {
            RootMap = rootMap,
            ValueLayout = new ValueFrameLayout(
                0,
                [],
                [],
                [],
                ImmutableHashSet<int>.Empty.Add(1)),
        };
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var request = CreateInstructionRequest(
            CilOperation.Call,
            context: context);
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));

        CreateEmitter(layouts, filterRoots).Emit(
            request,
            GetCodeWriter(request));

        var code = GetCodeBytes(request);
        Assert.Contains(expectedConstant, code);
        Assert.Contains(expectedAdd, code);
        Assert.Equal(1, filterRoots.Calls);
    }

    [Fact]
    public void ReturnsWithoutSafepointOrWhenRootSlotsAreAbsent()
    {
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var noSafepoint = CreateInstructionRequest(
            CilOperation.Call,
            context: CreateMethodEmissionContext());
        CreateEmitter(filterRoots: filterRoots).Emit(
            noSafepoint,
            GetCodeWriter(noSafepoint));

        var emptySlotsMap = new MethodRootMap(
            EntryKey,
            [],
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [], [])));
        var noSlots = CreateInstructionRequest(
            CilOperation.Call,
            context: CreateMethodEmissionContext() with { RootMap = emptySlotsMap });
        CreateEmitter(filterRoots: filterRoots).Emit(
            noSlots,
            GetCodeWriter(noSlots));

        Assert.Equal(0, filterRoots.Calls);
        Assert.Empty(GetCodeBytes(noSafepoint));
        Assert.Empty(GetCodeBytes(noSlots));
    }

    [Fact]
    public void ConstructorPublicationStopsAfterFilterEnvironmentWhenNoConstructorRootsExist()
    {
        var source = new RootSource(RootSourceKind.Argument, 0);
        var rootMap = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [source], [])));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            context: CreateMethodEmissionContext() with { RootMap = rootMap });
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();

        CreateEmitter(filterRoots: filterRoots).Emit(
            request,
            GetCodeWriter(request),
            constructorCall: true);

        Assert.Equal(1, filterRoots.Calls);
        Assert.Empty(GetCodeBytes(request));
    }

    [Fact]
    public void UnknownRootSourceIsRejectedDeterministically()
    {
        var unknown = new RootSource((RootSourceKind)99, 0);
        var rootMap = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(unknown, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [unknown], [unknown])));
        var request = CreateInstructionRequest(
            CilOperation.Call,
            context: CreateMethodEmissionContext() with { RootMap = rootMap });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter().Emit(request, GetCodeWriter(request)));

        Assert.Contains("Unknown root source", exception.Message);
    }

    private static IRootPublicationEmitter CreateEmitter(
        RecordingLayoutProvider? layouts = null,
        RecordingFilterEnvironmentRootEmitter? filterRoots = null)
    {
        layouts ??= new RecordingLayoutProvider();
        filterRoots ??= new RecordingFilterEnvironmentRootEmitter();
        return new[]
        {
            new RootPublicationEmitter(layouts, filterRoots),
        }.Cast<IRootPublicationEmitter>().Single();
    }

    private static void EmitThroughCapability(
        IRootPublicationEmitter emitter,
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        bool constructorCall = false) =>
        emitter.Emit(request, code, constructorCall);

    private sealed class RecordingFilterEnvironmentRootEmitter :
        IFilterEnvironmentRootEmitter
    {
        public int Calls { get; private set; }

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context) => Calls++;
    }
}
