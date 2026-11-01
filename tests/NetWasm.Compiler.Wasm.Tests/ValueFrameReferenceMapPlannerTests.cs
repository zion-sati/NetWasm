using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// A collection traces the words of a value frame its map names, so the map
// names each reference in storage whose frame memory holds the only copy, and
// nothing a Wasm local, a lane local or a filter's copy holds instead.
public sealed class ValueFrameReferenceMapPlannerTests
{
    private static readonly CliTypeIdentity Reference =
        CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, isValueType: false);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Test", "Pair", true);
    private static readonly CliTypeIdentity Packed = CliTypeIdentity.Named(Assembly, "Test", "Packed", true);
    private static readonly CliTypeIdentity Scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MapsEveryReferenceInFrameStorageThatIsTheOnlyCopy(WasmTarget target)
    {
        var size = WasmTargetLayout.For(target).ObjectReferenceSize;
        // Local 0 a reference, 1 a pair whose second word is one, 2 a scalar, 3 a
        // promoted reference, 4 a reference a filter captures, 5 a pair in lane
        // locals; argument 0 a reference.
        var layout = new ValueFrameLayout(32 * size,
            ImmutableDictionary<int, int>.Empty.Add(0, 0).Add(1, 2 * size).Add(2, 4 * size).Add(3, 5 * size)
                .Add(4, 6 * size).Add(5, 8 * size),
            ImmutableDictionary<int, int>.Empty.Add(0, 12 * size), [], [])
        {
            AddressTakenScalarPromotions = new(ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(3, Reference), []),
        };
        var filters = FilterEnvironmentLayout.Empty with
        {
            Captures = ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(new(false, 4), new(new(false, 4), Reference, 0, [])),
        };
        var aggregates = ManagedAggregateSsaPlan.Empty with
        {
            Storages = ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty.Add(
                new(ManagedAggregateStorageKind.Local, 5), null!),
        };

        var map = Planner(target).Plan(Header([Reference, Pair, Scalar, Reference, Reference, Pair]), layout, filters, aggregates);

        Assert.Equal(new[] { (1UL << 0) | (1UL << 3) | (1UL << 12) }, map.Words.ToArray());
        Assert.True(map.Storages.SetEquals([(false, 0), (false, 1), (true, 0)]));
    }

    // A reference past the sixty-fourth word needs a further word, and a
    // reference the target does not align keeps its publication.
    [Fact]
    public void LaterWordsHoldLaterReferencesAndUnalignedOnesStayUnmapped()
    {
        var layout = new ValueFrameLayout(512,
            ImmutableDictionary<int, int>.Empty.Add(0, 280).Add(1, 0), [], [], []);

        var map = Planner(WasmTarget.Wasm32).Plan(Header([Reference, Packed]), layout, FilterEnvironmentLayout.Empty, null);

        Assert.Equal(new[] { 0UL, 1UL << 6 }, map.Words.ToArray());
        Assert.True(map.Storages.SetEquals([(false, 0)]));
    }

    // A local's custom modifiers leave the type it holds, here a reference.
    [Fact]
    public void AModifiedLocalIsMappedAsTheTypeItModifies()
    {
        var modified = CliTypeIdentity.Modified(Reference, CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "IsVolatile", false), true);
        var layout = new ValueFrameLayout(16, ImmutableDictionary<int, int>.Empty.Add(0, 0), [], [], []);

        var map = Planner(WasmTarget.Wasm32).Plan(Header([modified]), layout, FilterEnvironmentLayout.Empty, null);

        Assert.Equal(1UL, Assert.Single(map.Words));
    }

    [Fact]
    public void AFrameWithoutReferencesHasNoMap()
    {
        var layout = new ValueFrameLayout(16, ImmutableDictionary<int, int>.Empty.Add(0, 0), [], [], []);

        Assert.Same(ValueFrameReferenceMap.Empty,
            Planner(WasmTarget.Wasm32).Plan(Header([Scalar]), layout, FilterEnvironmentLayout.Empty, null));
    }

    // Only a local's or an argument's own storage is covered: what a byref it
    // holds addresses, a stack value or an anchor keeps its publication, and
    // the remaining sources share slots again.
    [Fact]
    public void ExcludingCoveredSourcesLeavesTheRestAndAllocatesTheirSlotsAgain()
    {
        var map = new ValueFrameReferenceMap([1], [(false, 0), (true, 1)]);
        ImmutableArray<RootSource> roots =
        [
            new(RootSourceKind.Local, 0), new(RootSourceKind.Local, 0, 4), new(RootSourceKind.Argument, 1, 4),
            new(RootSourceKind.Local, 2), new(RootSourceKind.ManagedAddressLocal, 0), new(RootSourceKind.EvaluationStack, 0),
        ];
        var rootMap = new MethodRootMap(EntryKey,
            roots.Select((source, slot) => (source, slot)).ToImmutableDictionary(item => item.source, item => item.slot),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(3, new(3, roots, roots)));

        var excluded = map.Exclude(rootMap, new RootSlotAllocator());

        ImmutableArray<RootSource> kept =
            [new(RootSourceKind.Local, 2), new(RootSourceKind.ManagedAddressLocal, 0), new(RootSourceKind.EvaluationStack, 0)];
        Assert.Equal<RootSource>(kept, excluded.Safepoints[3].Roots);
        Assert.Equal<RootSource>(kept, excluded.Safepoints[3].ConstructorCallRoots);
        Assert.True(excluded.Slots.Keys.ToHashSet().SetEquals(kept));
        Assert.Equal(3, excluded.SlotCount);
        Assert.Same(rootMap, ValueFrameReferenceMap.Empty.Exclude(rootMap, new RootSlotAllocator()));
    }

    private static ValueFrameReferenceMapPlanner Planner(WasmTarget target) =>
        new(new RecordingLayoutProvider(WasmTargetLayout.For(target)), new Values(target), new Arguments());

    private static StructuredMethodHeader Header(ImmutableArray<CliTypeIdentity> locals) =>
        new StructuredMethodHeader(new FakeProgram().GetMethod(EntryKey), null, 1, [], [], []) with
        {
            Locals = [.. locals.Select(static type => type.StackKind)],
            LocalSignatureTypes = locals,
        };

    private sealed class Values(WasmTarget target) : IValueLayoutProvider
    {
        private readonly int _size = WasmTargetLayout.For(target).ObjectReferenceSize;

        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            type == Pair ? new(type, 2 * _size, _size, [_size]) :
            type == Packed ? new(type, 8, 1, [2]) :
            new(type, 4, 4, []);
    }

    private sealed class Arguments : IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) => Reference;
    }
}
