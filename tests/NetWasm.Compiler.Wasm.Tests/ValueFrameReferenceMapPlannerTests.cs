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

// A collection traces the words of a value frame its map names, and a callee
// publishes only the byrefs it receives, so the map names each reference in
// storage a byref can address. Storage whose current value can live elsewhere,
// in lane locals or a filter's environment, keeps its publication as well.
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
    public void MapsEveryReferenceInFrameStorageAByrefCanAddress(WasmTarget target)
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

        Assert.Equal(new[] { (1UL << 0) | (1UL << 3) | (1UL << 6) | (1UL << 9) | (1UL << 12) }, map.Words.ToArray());
        Assert.True(map.Storages.SetEquals([(false, 0), (false, 1), (true, 0)]));
        // Locals start zeroed and arguments copied.
        Assert.Empty(map.EntryClears);
    }

    // An aggregate argument split into lanes is read from the caller's copy, so
    // its own storage is zeroed before the first safepoint can trace it.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AnArgumentSplitIntoLanesIsMappedAndZeroedAtEntry(WasmTarget target)
    {
        var size = WasmTargetLayout.For(target).ObjectReferenceSize;
        var layout = new ValueFrameLayout(4 * size, [], ImmutableDictionary<int, int>.Empty.Add(0, 2 * size), [], []);
        var aggregates = ManagedAggregateSsaPlan.Empty with
        {
            Storages = ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty.Add(
                new(ManagedAggregateStorageKind.Argument, 0), null!),
        };

        var map = Planner(target, Pair).Plan(Header([]), layout, FilterEnvironmentLayout.Empty, aggregates);

        Assert.Equal(new[] { 1UL << 3 }, map.Words.ToArray());
        Assert.Empty(map.Storages);
        Assert.Equal(new[] { (3 * size, size) }, map.EntryClears);
    }

    // A filter's argument capture holds a copy made at the frame's entry; a
    // captured byref is mapped as the interior pointer it is.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FilterCapturesAreMappedWhereTheEnvironmentHoldsThem(WasmTarget target)
    {
        var size = WasmTargetLayout.For(target).ObjectReferenceSize;
        var layout = new ValueFrameLayout(8 * size, [], [], [], []);
        var filters = FilterEnvironmentLayout.Empty with
        {
            Captures = ImmutableDictionary<CapturedSlot, FilterCapture>.Empty
                .Add(new(true, 0), new(new(true, 0), Reference, 4 * size, []))
                .Add(new(true, 1), new(new(true, 1), CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress), 5 * size, []))
                .Add(new(false, 0), new(new(false, 0), Scalar, 6 * size, [])),
        };

        var map = Planner(target).Plan(Header([Scalar]), layout, filters, null);

        Assert.Equal(new[] { (1UL << 4) | (1UL << 5) }, map.Words.ToArray());
        Assert.Empty(map.Storages);
        Assert.Empty(map.EntryClears);
    }

    // A value type's constructor receives its newobj temporary's address. The
    // temporary is zeroed only when newobj runs, so the entry zeroes its
    // references first, adjacent words as one range.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConstructedValueTemporariesAreMappedAndZeroedAtEntry(WasmTarget target)
    {
        var size = WasmTargetLayout.For(target).ObjectReferenceSize;
        var layout = new ValueFrameLayout(16 * size, [], [],
            ImmutableDictionary<int, int>.Empty.Add(3, 2 * size).Add(9, 4 * size).Add(20, 8 * size).Add(30, 12 * size), [])
        {
            ConstructedValueTypes = ImmutableDictionary<int, CliTypeIdentity>.Empty
                .Add(3, Pair).Add(9, Pair).Add(20, Pair).Add(30, Scalar),
        };

        var map = Planner(target).Plan(Header([]), layout, FilterEnvironmentLayout.Empty, null);

        Assert.Equal(new[] { (1UL << 3) | (1UL << 5) | (1UL << 9) }, map.Words.ToArray());
        Assert.Empty(map.Storages);
        Assert.Equal(new[] { (3 * size, size), (5 * size, size), (9 * size, size) }, map.EntryClears);
        var adjacent = layout with
        {
            TemporaryOffsets = ImmutableDictionary<int, int>.Empty.Add(3, 0).Add(9, size),
            ConstructedValueTypes = ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(3, Reference).Add(9, Reference),
        };
        Assert.Equal(new[] { (0, 2 * size) },
            Planner(target).Plan(Header([]), adjacent, FilterEnvironmentLayout.Empty, null).EntryClears);
    }

    // A reference past the sixty-fourth word needs a further word.
    [Fact]
    public void LaterWordsHoldLaterReferences()
    {
        var layout = new ValueFrameLayout(512, ImmutableDictionary<int, int>.Empty.Add(0, 280), [], [], []);

        var map = Planner(WasmTarget.Wasm32).Plan(Header([Reference]), layout, FilterEnvironmentLayout.Empty, null);

        Assert.Equal(new[] { 0UL, 1UL << 6 }, map.Words.ToArray());
        Assert.True(map.Storages.SetEquals([(false, 0)]));
    }

    // Layouts align every reference to its size, so one that is not is a
    // compiler fault the collector must never trace.
    [Fact]
    public void AnUnalignedReferenceIsACompilerFault()
    {
        var layout = new ValueFrameLayout(512, ImmutableDictionary<int, int>.Empty.Add(0, 0), [], [], []);

        var fault = Assert.Throws<CompilerException>(() =>
            Planner(WasmTarget.Wasm32).Plan(Header([Packed]), layout, FilterEnvironmentLayout.Empty, null));

        Assert.Equal(DiagnosticCode.CompilerInvariant, fault.Diagnostic.Code);
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

    private static ValueFrameReferenceMapPlanner Planner(WasmTarget target, CliTypeIdentity? argument = null) =>
        new(new RecordingLayoutProvider(WasmTargetLayout.For(target)), new Values(target), new Arguments(argument ?? Reference));

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

    private sealed class Arguments(CliTypeIdentity type) : IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) => type;
    }
}
