using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// Methods, root maps, units and stores for the compile's unit actors, in memory.
internal static class UnitActorFixture
{
    internal static readonly AssemblyIdentity Assembly = new("UnitActors");
    internal static readonly CliTypeIdentity Program = CliTypeIdentity.Named(Assembly, "Tests", "Program", isValueType: false);
    internal static readonly CliTypeIdentity Point = CliTypeIdentity.Named(Assembly, "Tests", "Point", isValueType: true);

    // static int M() { Point local; return 1; }, after an instruction naming the given
    // operand, if any.
    internal static ManagedMethodBody Method(int token = 0x06000001, CilOperand? naming = null)
    {
        var definition = new MethodDefinitionModel(new EntityKey(Assembly, token), new EntityKey(Assembly, 0x02000001),
            "M" + token.ToString("x", CultureInfo.InvariantCulture), true, MethodSignatureModel.Create(CliValueKind.I4), 0);
        var instance = new MethodInstanceModel(definition, Program, [], definition.Signature);
        var start = naming is null ? 0 : 1;
        CilInstruction[] prefix = naming is null ? [] : [new CilInstruction(0, 1, CilOperation.Nop, naming)];
        var body = new CilMethodBody(definition, 1, [CliValueKind.ValueType],
        [
            .. prefix,
            new CilInstruction(start, start + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            new CilInstruction(start + 1, start + 2, CilOperation.Return, new CilOperand.None()),
        ])
        {
            MethodInstance = instance,
            LocalSignatureTypes = [Point],
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        return new(instance, new ValidatedControlFlowGraph(graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, [])));
    }

    internal static string Name(ManagedMethodBody method) => method.Method.CanonicalName;

    internal static StructuredMethod Structured(ManagedMethodBody method) => new(
        StructuredMethodHeader.Of(method.Body), new(0), ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty,
        StructuredSequence.Empty, [], ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);

    // A root map with a safepoint at the given offset, and an anchor of the given type.
    internal static MethodRootMap RootMap(ManagedMethodBody method, int safepoint = 1, CliTypeIdentity? anchor = null) =>
        new(method.Method.Definition.Key, ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Local, 0), 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(safepoint, new(safepoint, [new(RootSourceKind.Local, 0)], [])))
        {
            AnchorPlan = anchor is null
                ? StackStorageAnchorPlan.Empty
                : new([new(new(StackStorageLocationKind.Local, 0), anchor)],
                    ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty),
        };

    internal static CompiledUnitState State(params ManagedMethodBody[] methods) => new("units")
    {
        Policy = "Wasm32/Speed/Disabled",
        Bodies = CompiledUnitState.Index(methods, []),
    };

    // A unit of the method, its input as the state fingerprints it.
    internal static CompiledUnit Unit(CompiledUnitState state, ManagedMethodBody method, MethodRootMap rootMap,
        ImmutableArray<UnitDependency> queries = default, ImmutableArray<UnitRelocation> relocations = default,
        byte[]? body = null) =>
        new(Name(method), state.InputOf(Name(method), rootMap)!.Value, queries.IsDefault ? [] : queries,
            [.. body ?? [0x00, 0x41, 0x01, 0x0B]], relocations.IsDefault ? [] : relocations, 2)
        {
            FunctionName = "f",
            Result = CliValueKind.I4,
        };

    // One segment per list, in order; each segment's bytes by sequence.
    internal static (UnitStore Store, Dictionary<int, byte[]> Segments) Segments(params CompiledUnit[][] segments)
    {
        var bytes = new Dictionary<int, byte[]>();
        var manifestSegments = new List<UnitManifestSegment>();
        var entries = new List<UnitManifestEntry>();
        for (var index = 0; index < segments.Length; index++)
        {
            var writer = new UnitSegmentWriter();
            foreach (var unit in segments[index]) writer.Add(unit);
            bytes[index + 1] = Pinned(writer);
            manifestSegments.Add(new(index + 1, writer.Count, writer.Length));
            var ordinal = index;
            entries.AddRange(writer.Entries.Select(entry => new UnitManifestEntry(entry.Hash, ordinal, entry.Offset)));
        }
        var manifest = UnitManifestFormat.Write(manifestSegments, entries, segments.Length + 1);
        return (UnitStore.Over(manifest, sequence => new PinnedUnitSegmentMemory(bytes[sequence])), bytes);
    }

    internal static UnitStore Store(params CompiledUnit[] units) => Segments([units]).Store;

    // The unit's record with a negative count, which reads as corrupt.
    internal static UnitStore Corrupt(CompiledUnit unit)
    {
        var (store, segments) = Segments([unit]);
        var location = store.Find(unit.Identity)!.Value;
        store.Dispose();
        var segment = segments[1];
        var records = BinaryPrimitives.ReadInt64LittleEndian(segment.AsSpan(56));
        BinaryPrimitives.WriteInt32LittleEndian(segment.AsSpan((int)records + location.Offset + 56), -1);
        return UnitStore.Over(UnitManifestFormat.Write([new(1, 1, segment.Length)],
            [new(UnitStoreFormat.HashOf(unit.Identity), 0, location.Offset)], 2), _ => new PinnedUnitSegmentMemory(segment));
    }

    internal static byte[] Pinned(UnitSegmentWriter writer)
    {
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)writer.Length), pinned: true);
        using var stream = new MemoryStream(bytes, writable: true);
        writer.WriteTo(stream);
        return bytes;
    }

    internal static WasmFunctionSymbols Symbols(params (uint Index, string Symbol)[] functions) => new(
        functions.ToFrozenDictionary(function => function.Index, function => function.Symbol),
        functions.ToFrozenDictionary(function => function.Symbol, function => function.Index, StringComparer.Ordinal));

    internal static ManagedBodyEmission Emission(byte[] body, FilterEnvironmentLayout? environment = null) => new(
        new("f", WasmFunctionType.Create(CliValueKind.I4), body) { IsNoInlining = true }, "env",
        environment ?? FilterEnvironmentLayout.Empty,
        new("record", "key", new(body, 2, 0, null, "key", FilterEnvironmentLayout.Empty,
            ImmutableDictionary<int, int>.Empty.Add(0, 1))))
    {
        Dependencies = [new(UnitQueryKind.StringTypeId, "", new(5, 6))],
    };
}

// A unit's input is its method's analysed body, the structuring policy and its root map.
public sealed class CompiledUnitStateTests
{
    [Fact]
    public void AMethodWithoutAnAnalysedBodyHasNoInput()
    {
        var method = UnitActorFixture.Method();

        Assert.Null(UnitActorFixture.State().InputOf(UnitActorFixture.Name(method), UnitActorFixture.RootMap(method)));
    }

    // Fingerprinted once per root map: another map is another input.
    [Fact]
    public void AnInputIsTheBodyPolicyAndRootMap()
    {
        var method = UnitActorFixture.Method();
        var state = UnitActorFixture.State(method);
        var map = UnitActorFixture.RootMap(method);

        var input = state.InputOf(UnitActorFixture.Name(method), map);

        Assert.Equal(input, state.InputOf(UnitActorFixture.Name(method), map));
        Assert.NotEqual(input, state.InputOf(UnitActorFixture.Name(method), UnitActorFixture.RootMap(method, safepoint: 2)));
        using var otherPolicy = new CompiledUnitState("units") { Policy = "Wasm64/Speed/Disabled", Bodies = state.Bodies };
        Assert.NotEqual(input, otherPolicy.InputOf(UnitActorFixture.Name(method), map));
    }

    [Fact]
    public void ASymbolResolvesToItsFunctionOnceTheModuleIsPlanned()
    {
        using var state = UnitActorFixture.State();

        Assert.False(state.TryResolve("method:Callee", out _));
        state.Symbols = UnitActorFixture.Symbols((7, "method:Callee"));
        Assert.True(state.TryResolve("method:Callee", out var index));
        Assert.Equal(7u, index);
    }
}

// An emitted method is kept as a unit when its body can be rebound in a later module: every
// function it calls has a stable name; a restored one stays where it is.
public sealed class CompiledUnitCapturerTests
{
    private static readonly byte[] Body = [0x00, 0x10, 0x07, 0x41, 0x03, 0x0B];

    private static readonly ImmutableArray<WasmIndexOperand> Calls =
    [
        new(2, 1, WasmIndexSpace.Function, 7, false),
        new(4, 1, WasmIndexSpace.Type, 3, false),
    ];

    private static (CompiledUnitState State, ManagedBodyEmission Emission, MethodRootMap RootMap, string Identity) Captured(
        Action<CompiledUnitState, string>? arrange = null, ManagedBodyEmission? emission = null, Scanner? scanner = null,
        bool body = true)
    {
        var method = UnitActorFixture.Method();
        var state = body ? UnitActorFixture.State(method) : UnitActorFixture.State();
        state.Symbols = UnitActorFixture.Symbols((7, "method:Callee"));
        var identity = UnitActorFixture.Name(method);
        arrange?.Invoke(state, identity);
        var rootMap = UnitActorFixture.RootMap(method);
        emission ??= UnitActorFixture.Emission(Body);
        new CompiledUnitCapturer(state, scanner ?? new Scanner(Calls), new NoGroups())
            .Capture(identity, emission, UnitActorFixture.Structured(method), rootMap);
        return (state, emission, rootMap, identity);
    }

    [Fact]
    public void AnEmittedMethodIsKeptWithItsRelocationsByName()
    {
        var (state, emission, _, identity) = Captured();

        var unit = Assert.Single(state.Captured);
        Assert.Equal(1, state.Emitted);
        Assert.Equal(identity, unit.Identity);
        Assert.Equal([("method:Callee", 2), ("Type:3", 4)], unit.Relocations.Select(relocation => (relocation.Symbol, relocation.Offset)));
        Assert.Equal(Body, unit.Body);
        Assert.Equal(("f", CliValueKind.I4, true, "env", 2), (unit.FunctionName, unit.Result, unit.IsNoInlining, unit.EnvironmentKey,
            unit.InstructionCount));
        Assert.Equal(emission.Dependencies, unit.Dependencies);
        Assert.Empty(unit.ExceptionGroups!.Value);
        // No root-map analysis recorded what it read: the unit keeps no root map.
        Assert.Null(unit.RootMap);
        Assert.Empty(unit.RootMapDependencies);
    }

    [Fact]
    public void ARootMapIsKeptWithTheFactsItsAnalysisRead()
    {
        ImmutableArray<UnitDependency> facts = [new(UnitQueryKind.AllocatingMethod, "UnitActors:0x06000002", new(1, 1))];

        var (state, _, rootMap, _) = Captured((state, identity) => state.RootMapDependencies[identity] = facts);

        var unit = Assert.Single(state.Captured);
        Assert.Equal(UnitRootMapFormat.Write(rootMap), unit.RootMap);
        Assert.Equal(facts, unit.RootMapDependencies);
    }

    // A root map taken from a unit keeps that unit's facts; one whose unit cannot be read keeps none.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARootMapTakenFromAUnitKeepsThatUnitsFacts(bool corrupt)
    {
        var method = UnitActorFixture.Method();
        ImmutableArray<UnitDependency> facts = [new(UnitQueryKind.AllocatingMethod, "UnitActors:0x06000002", new(1, 1))];

        var (state, _, rootMap, _) = Captured((state, identity) =>
        {
            var source = UnitActorFixture.Unit(state, method, UnitActorFixture.RootMap(method)) with
            {
                RootMap = UnitRootMapFormat.Write(UnitActorFixture.RootMap(method)),
                RootMapDependencies = facts,
            };
            using (var healthy = UnitActorFixture.Store(source)) state.RootMapSources[identity] = healthy.Find(identity)!.Value;
            state.Store = corrupt ? UnitActorFixture.Corrupt(source) : UnitActorFixture.Store(source);
        });

        var unit = Assert.Single(state.Captured);
        Assert.Equal(corrupt ? null : UnitRootMapFormat.Write(rootMap), unit.RootMap);
        Assert.Equal(corrupt ? [] : facts, unit.RootMapDependencies);
    }

    [Fact]
    public void ARootMapTakenFromAUnitWhoseSegmentDidNotOpenKeepsNone()
    {
        var (state, _, _, _) = Captured((state, identity) =>
        {
            state.Store = UnitStore.Over(UnitManifestFormat.Write([new(1, 1, 64)], [new(UnitStoreFormat.HashOf(identity), 0, 64)], 2),
                _ => null);
            state.RootMapSources[identity] = new UnitLocation(0, 64);
        });

        var unit = Assert.Single(state.Captured);
        Assert.Null(unit.RootMap);
        Assert.Empty(unit.RootMapDependencies);
    }

    [Fact]
    public void ARestoredMethodStaysInItsUnit()
    {
        var location = new UnitLocation(0, 64);
        var restored = UnitActorFixture.Emission(Body) with { Unit = location };

        var (state, _, _, _) = Captured(emission: restored);

        Assert.Equal(1, state.Restored);
        Assert.Equal([location], state.Carried);
        Assert.Empty(state.Captured);
    }

    // Each reason a body cannot be rebound later keeps it from being a unit.
    [Theory]
    [InlineData("filter environment")]
    [InlineData("unscannable")]
    [InlineData("unnamed function")]
    [InlineData("no body")]
    public void ABodyThatCannotBeReboundIsNotKept(string reason)
    {
        var (state, _, _, _) = reason switch
        {
            "filter environment" => Captured(emission: UnitActorFixture.Emission(Body, FilterEnvironmentLayout.Empty with { Size = 8 })),
            "unscannable" => Captured(scanner: new Scanner(null)),
            "unnamed function" => Captured((state, _) => state.Symbols = WasmFunctionSymbols.Empty),
            _ => Captured(body: false),
        };

        Assert.Empty(state.Captured);
        Assert.Equal(1, state.Emitted);
    }

    // Without a directory, units are off: nothing is kept or carried.
    [Fact]
    public void WithoutADirectoryNothingIsKept()
    {
        var method = UnitActorFixture.Method();
        using var state = new CompiledUnitState(null) { Bodies = CompiledUnitState.Index([method], []) };
        var capturer = new CompiledUnitCapturer(state, new Scanner(Calls), new NoGroups());

        capturer.Capture(UnitActorFixture.Name(method), UnitActorFixture.Emission(Body), UnitActorFixture.Structured(method),
            UnitActorFixture.RootMap(method));
        capturer.Capture(UnitActorFixture.Name(method), UnitActorFixture.Emission(Body) with { Unit = new(0, 64) },
            UnitActorFixture.Structured(method), UnitActorFixture.RootMap(method));

        Assert.Empty(state.Captured);
        Assert.Empty(state.Carried);
        Assert.Equal((1, 1), (state.Emitted, state.Restored));
    }

    internal sealed class Scanner(ImmutableArray<WasmIndexOperand>? operands) : IWasmIndexOperandScanner
    {
        public ImmutableArray<WasmIndexOperand> Scan(ReadOnlySpan<byte> body) =>
            operands ?? throw new InvalidDataException("An instruction cannot be decoded.");
    }

    private sealed class NoGroups : IExceptionGroupEnumerator
    {
        public IEnumerable<StructuredExceptionGroupId> Enumerate(StructuredMethod method) => [];
    }
}

// Every environment part makes a filter environment; where an empty one sits does not.
public sealed class FilterEnvironmentLayoutTests
{
    [Fact]
    public void AnEnvironmentIsEmptyWithoutAFrameRootsCapturesOrAnchors()
    {
        Assert.True(FilterEnvironmentLayout.Empty.IsEmpty);
        Assert.True((FilterEnvironmentLayout.Empty with { RootFrameOffset = 16, ParentRootFrameOffset = 8 }).IsEmpty);
        Assert.False((FilterEnvironmentLayout.Empty with { Size = 8 }).IsEmpty);
        Assert.False((FilterEnvironmentLayout.Empty with { RootSlotCount = 1 }).IsEmpty);
        Assert.False((FilterEnvironmentLayout.Empty with { AnchorOffsets = ImmutableDictionary<int, int>.Empty.Add(0, 8) }).IsEmpty);
        Assert.False((FilterEnvironmentLayout.Empty with
        {
            Captures = ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(default, default!),
        }).IsEmpty);
    }
}

// Publishing keeps restored units where they are and writes emitted ones into one new
// segment; mostly dead segments, and all but the largest once there are too many, merge in.
public sealed class CompiledUnitPublisherTests
{
    private static CompiledUnit Unit(string identity, int size = 4) => new(identity, new(1, 1), [],
        [.. Enumerable.Repeat((byte)0x01, size)], [], 1)
    {
        FunctionName = identity,
    };

    // Publishes with the given units carried and captured; the store the change gives.
    private static (UnitStoreChange Change, UnitStore Next) Publish(CompiledUnit[][] segments, string[] carried,
        CompiledUnit[] captured, Action<CompiledUnitState>? arrange = null)
    {
        var (store, bytes) = UnitActorFixture.Segments(segments);
        var state = new CompiledUnitState("units") { Store = store };
        foreach (var identity in carried) state.Carried.Add(store.Find(identity)!.Value);
        foreach (var unit in captured) state.Captured.Add(unit);
        arrange?.Invoke(state);

        new CompiledUnitPublisher(state).Publish();

        var change = state.Change!.Value;
        if (change.Segment is { } writer) bytes[change.Sequence] = UnitActorFixture.Pinned(writer);
        return (change, change.Manifest is { } manifest
            ? UnitStore.Over(manifest, sequence => new PinnedUnitSegmentMemory(bytes[sequence]))
            : UnitStore.Empty);
    }

    [Fact]
    public void AStoreWhoseUnitsAreAllRestoredIsUnchanged()
    {
        var (change, _) = Publish([[Unit("A"), Unit("B")]], ["A", "B"], []);

        Assert.Null(change.Manifest);
        Assert.Null(change.Segment);
        Assert.StartsWith("restored 0 emitted 0 kept 2 in 1 segments;", change.Summary, StringComparison.Ordinal);
    }

    // Units no longer restored drop out of the manifest; their segments stay while mostly live.
    [Fact]
    public void ADroppedUnitRewritesTheManifestAlone()
    {
        var (change, next) = Publish([[Unit("A"), Unit("B"), Unit("C"), Unit("D")], [Unit("E")]], ["A", "C", "E"], []);

        Assert.Null(change.Segment);
        Assert.Equal([1, 2], change.LiveSequences.Order());
        string[] kept = ["A", "C", "E"], dropped = ["B", "D"];
        Assert.All(kept, identity => Assert.NotNull(next.Find(identity)));
        Assert.All(dropped, identity => Assert.Null(next.Find(identity)));
    }

    // An emitted unit replaces the restored one of its method.
    [Fact]
    public void EmittedUnitsGoIntoTheNewSegmentWithMostlyDeadSegmentsMerged()
    {
        var (change, next) = Publish([[Unit("A"), Unit("B"), Unit("C")], [Unit("D"), Unit("E")]], ["A", "D", "E"],
            [Unit("E", size: 8), Unit("F")]);

        Assert.Equal(3, change.Sequence);
        Assert.Equal(3, change.Segment!.Count);
        Assert.Equal([2, 3], change.LiveSequences.Order());
        Assert.Equal(next.Find("F")!.Value.Segment, next.Find("A")!.Value.Segment);
        Assert.Equal(8, next.Segments[next.Find("E")!.Value.Segment]!.Record(next.Find("E")!.Value.Offset).Body.Length);
        Assert.NotEqual(next.Find("D")!.Value.Segment, next.Find("A")!.Value.Segment);
    }

    // Once the segments would reach their budget, all but the largest merge into the new one.
    [Fact]
    public void TooManySegmentsMergeIntoTheNewOneButTheLargest()
    {
        var segments = Enumerable.Range(0, 16).Select(index => new[] { Unit($"U{index}", index == 5 ? 64 : 4) }).ToArray();

        var (change, next) = Publish(segments, [.. segments.Select(segment => segment[0].Identity)], [Unit("New")]);

        Assert.Equal(16, change.Segment!.Count);
        Assert.Equal([6, 17], change.LiveSequences.Order());
        Assert.All(segments, segment => Assert.NotNull(next.Find(segment[0].Identity)));
    }

    [Fact]
    public void TheSummaryNamesWhyUnitsWereNotReused()
    {
        var (change, _) = Publish([[Unit("A")]], [], [Unit("B")], state =>
        {
            state.UnbuildableKinds[UnitQueryKind.ValueLayout] = 2;
            state.UnbuildableKinds[UnitQueryKind.ObjectLayout] = 1;
            state.ChangedKinds[UnitQueryKind.StringLayout] = 3;
            state.UnbuildableKeys["ValueLayout T (asked by M)"] = 0;
        });

        Assert.Contains("\nunbuildable: ValueLayout 2, ObjectLayout 1\nchanged: StringLayout 3\nValueLayout T (asked by M)\n",
            change.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutADirectoryNothingIsPublished()
    {
        using var state = new CompiledUnitState(null);

        new CompiledUnitPublisher(state).Publish();

        Assert.Null(state.Change);
    }
}

// A method is restored from its unit when its input fingerprints the same, every query the
// unit recorded answers the same, and every symbol its body names exists; each miss is counted.
public sealed class CompiledUnitRestorerTests
{
    private static readonly UnitFingerprint Hello = new(1, 2);

    private static (CompiledUnitState State, ManagedMethodBody[] Methods) State(int count = 1)
    {
        var methods = Enumerable.Range(1, count).Select(index => UnitActorFixture.Method(0x06000000 + index)).ToArray();
        return (UnitActorFixture.State(methods), methods);
    }

    private static ManagedBodyEmission? Restore(CompiledUnitState state, ManagedMethodBody method, Replayer? replayer = null) =>
        new CompiledUnitRestorer(state, replayer ?? new Replayer(), new WasmIndexOperandRewriter()).Restore(
            UnitActorFixture.Name(method), "record", UnitActorFixture.Structured(method), UnitActorFixture.RootMap(method), null!);

    [Theory]
    [InlineData("")]
    [InlineData("env")]
    public void AUnitWhoseInputAndSymbolsHoldIsRestoredWithItsCallsRebound(string environment)
    {
        var (state, methods) = State();
        state.Symbols = UnitActorFixture.Symbols((9, "method:Callee"));
        var unit = UnitActorFixture.Unit(state, methods[0], UnitActorFixture.RootMap(methods[0]),
            relocations: [new(2, 1, WasmIndexSpace.Function, "method:Callee", false), new(4, 1, WasmIndexSpace.Type, "Type:3", false)],
            body: [0x00, 0x10, 0x00, 0x41, 0x00, 0x0B]) with
        {
            EnvironmentKey = environment,
            Parameters = [CliValueKind.I8],
            IsAggressiveInlining = true,
            BlockEmissionCounts = ImmutableDictionary<int, int>.Empty.Add(0, 2),
        };
        state.Store = UnitActorFixture.Store(unit);

        var restored = Restore(state, methods[0]);

        Assert.NotNull(restored);
        Assert.Equal(state.Store.Find(unit.Identity), restored.Unit);
        Assert.Equal([0x00, 0x10, 0x09, 0x41, 0x03, 0x0B], restored.Function.Body);
        Assert.Equal(("f", CliValueKind.I8, CliValueKind.I4, true), (restored.Function.Name, Assert.Single(restored.Function.Type.Parameters),
            restored.Function.Type.Result, restored.Function.IsAggressiveInlining));
        Assert.Equal((environment, "record", 2), (restored.EnvironmentKey, restored.Record.Identity,
            restored.Record.Emission.OriginalBlockEmissionCounts[0]));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("input")]
    [InlineData("symbol")]
    [InlineData("corrupt")]
    public void AUnitThatIsMissingStaleOrDamagedIsNotRestored(string miss)
    {
        var (state, methods) = State();
        var unit = UnitActorFixture.Unit(state, methods[0], UnitActorFixture.RootMap(methods[0]),
            relocations: [new(2, 1, WasmIndexSpace.Function, "method:Callee", false)], body: [0x00, 0x10, 0x00, 0x0B]);
        state.Symbols = UnitActorFixture.Symbols((9, miss == "symbol" ? "method:Other" : "method:Callee"));
        state.Store = miss switch
        {
            "missing" => UnitStore.Empty,
            "input" => UnitActorFixture.Store(unit with { Input = new(9, 9) }),
            "corrupt" => UnitActorFixture.Corrupt(unit),
            _ => UnitActorFixture.Store(unit),
        };

        Assert.Null(Restore(state, methods[0]));
        Assert.Equal(miss switch
        {
            "missing" => (1, 0, 0, 0),
            "input" => (0, 1, 0, 0),
            "symbol" => (0, 0, 1, 0),
            _ => (0, 0, 0, 1),
        }, (state.MissingUnits, state.InputChanged, state.UnresolvedSymbols, state.CorruptUnits));
    }

    // A recorded query answered differently, not rebuilt or failing keeps the unit stale; a
    // query the method's own identities cannot rebuild is asked again with the program's.
    [Theory]
    [InlineData("same", true)]
    [InlineData("changed", false)]
    [InlineData("unbuildable", false)]
    [InlineData("built from the program", true)]
    [InlineData("failed", false)]
    public void EachRecordedQueryMustAnswerTheSame(string answer, bool restored)
    {
        var (state, methods) = State();
        state.Store = UnitActorFixture.Store(UnitActorFixture.Unit(state, methods[0], UnitActorFixture.RootMap(methods[0]),
            queries: [new(UnitQueryKind.StringLayout, "hello", Hello)]));
        if (answer == "built from the program") state.Program = new(() => UnitQueryArguments.Of(state.Bodies.Values));
        var replayer = new Replayer(answer switch
        {
            "same" => [new(UnitQueryStatus.Answered, Hello)],
            "changed" => [new(UnitQueryStatus.Answered, new(3, 4))],
            "failed" => [new(UnitQueryStatus.Failed, default)],
            _ => [new(UnitQueryStatus.Unbuildable, default), new(UnitQueryStatus.Answered, Hello)],
        });

        Assert.Equal(restored, Restore(state, methods[0], replayer) is not null);
        // The segment's query is checked once.
        Assert.Equal(restored, Restore(state, methods[0], new Replayer()) is not null);
        Assert.Equal(answer switch
        {
            "changed" => (1, 0, 0),
            "unbuildable" => (0, 1, 0),
            "failed" => (0, 0, 2),
            _ => (0, 0, 0),
        }, (state.ChangedKinds.GetValueOrDefault(UnitQueryKind.StringLayout),
            state.UnbuildableKinds.GetValueOrDefault(UnitQueryKind.StringLayout), state.ProviderFailures));
        Assert.Equal(restored ? 0 : 2, state.ChangedAnswers + state.UnbuildableQueries + state.ProviderFailures);
    }

    // The summary names a few of the queries that could not be rebuilt, with the method that asked.
    [Fact]
    public void AFewUnbuildableQueriesAreNamed()
    {
        var (state, methods) = State(9);
        state.Store = UnitActorFixture.Store([.. methods.Select((method, index) => UnitActorFixture.Unit(state, method,
            UnitActorFixture.RootMap(method), queries: [new(UnitQueryKind.StringLayout, $"text{index}", Hello)]))]);
        var replayer = new Replayer(Enumerable.Repeat(new UnitQueryAnswer(UnitQueryStatus.Unbuildable, default), 9).ToArray());

        foreach (var method in methods) Assert.Null(Restore(state, method, replayer));

        Assert.Equal(9, state.UnbuildableQueries);
        Assert.Equal(8, state.UnbuildableKeys.Count);
        Assert.Contains($"StringLayout text0 (asked by {UnitActorFixture.Name(methods[0])})", state.UnbuildableKeys.Keys);
    }

    [Fact]
    public void AnUnbuildableQueryOfAMethodWithoutAnInstanceNamesItsDefinition()
    {
        var (state, methods) = State();
        state.Store = UnitActorFixture.Store(UnitActorFixture.Unit(state, methods[0], UnitActorFixture.RootMap(methods[0]),
            queries: [new(UnitQueryKind.StringLayout, "hello", Hello)]));
        var structured = UnitActorFixture.Structured(methods[0]);
        structured = structured with { Header = structured.Header with { MethodInstance = null } };

        Assert.Null(new CompiledUnitRestorer(state, new Replayer(new UnitQueryAnswer(UnitQueryStatus.Unbuildable, default)),
            new WasmIndexOperandRewriter()).Restore(UnitActorFixture.Name(methods[0]), "record", structured,
            UnitActorFixture.RootMap(methods[0]), null!));

        Assert.Equal($"StringLayout hello (asked by {methods[0].Method.Definition.Key})", Assert.Single(state.UnbuildableKeys.Keys));
    }

    private sealed class Replayer(params UnitQueryAnswer[] answers) : IUnitQueryReplayer
    {
        private int _next;

        public UnitQueryAnswer Replay(UnitQueryKind kind, string key, UnitQueryArguments arguments,
            NetWasm.Compiler.Wasm.Emission.Planning.InstructionModuleTarget target) =>
            _next < answers.Length ? answers[_next++] : throw new InvalidOperationException("A checked query was asked again.");
    }
}

// A method whose unit kept its root map takes that map instead of analysing, when the unit's
// input matches and every fact the analysis read still holds; under verification it is
// analysed too and must match. Otherwise the analysis runs with what it reads recorded.
public sealed class UnitRootMapRecorderTests
{
    private static readonly EntityKey Callee = new(UnitActorFixture.Assembly, 0x06000002);

    private sealed record World(bool Allocates = true, Exception? LayoutFailure = null, Exception? SiteFailure = null);

    private static UnitRootMapRecorder Recorder(CompiledUnitState state, World world) => new(state,
        new Layouts(world.LayoutFailure), new Sites(world.SiteFailure),
        world.Allocates ? new HashSet<EntityKey> { Callee } : [], new HashSet<string> { "G<int>" }, new NoFields());

    // What a root-map analysis reads: a layout, a dispatch site and which callees allocate.
    private static Func<MethodRootMap> Analysis(UnitRootMapRecorder recorder, MethodRootMap map, List<string>? ran = null) => () =>
    {
        ran?.Add("analysed");
        _ = recorder.Values.GetValueLayout(UnitActorFixture.Program);
        _ = recorder.DispatchCallSites.TryGetValue("site", out _);
        _ = recorder.AllocatingMethods.Contains(Callee);
        _ = recorder.AllocatingConstructedMethods.Contains("G<int>");
        return map;
    };

    // The last compile: the method analysed, and its unit kept with the map and its facts.
    private static UnitStore Kept(ManagedMethodBody method, MethodRootMap map,
        Func<ImmutableArray<UnitDependency>, ImmutableArray<UnitDependency>>? facts = null, bool keepMap = true)
    {
        using var last = UnitActorFixture.State(method);
        var recorder = Recorder(last, new());
        recorder.Map(method, Analysis(recorder, map));
        var recorded = last.RootMapDependencies[UnitActorFixture.Name(method)];
        return UnitActorFixture.Store(UnitActorFixture.Unit(last, method, map) with
        {
            RootMap = keepMap ? UnitRootMapFormat.Write(map) : null,
            RootMapDependencies = facts?.Invoke(recorded) ?? recorded,
        });
    }

    [Fact]
    public void AMethodWithoutAUnitIsAnalysedWithWhatItReadsRecorded()
    {
        var method = UnitActorFixture.Method();
        using var state = UnitActorFixture.State(method);
        var recorder = Recorder(state, new());
        var map = UnitActorFixture.RootMap(method);

        Assert.Same(map, recorder.Map(method, Analysis(recorder, map)));

        Assert.Equal([UnitQueryKind.ValueLayout, UnitQueryKind.DispatchCallSite, UnitQueryKind.AllocatingMethod,
            UnitQueryKind.AllocatingConstructedMethod], state.RootMapDependencies[UnitActorFixture.Name(method)].Select(fact => fact.Kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKeptMapWhoseFactsHoldIsTaken(bool anchored)
    {
        var method = UnitActorFixture.Method();
        var map = UnitActorFixture.RootMap(method, anchor: anchored ? UnitActorFixture.Point : null);
        using var state = UnitActorFixture.State(method);
        state.Store = Kept(method, map);
        var ran = new List<string>();
        var recorder = Recorder(state, new());

        var taken = recorder.Map(method, Analysis(recorder, map, ran));

        Assert.Empty(ran);
        Assert.Equal(UnitFingerprintOf(map), UnitFingerprintOf(taken));
        Assert.Equal(1, state.RestoredRootMaps);
        Assert.Equal(state.Store.Find(UnitActorFixture.Name(method)), state.RootMapSources[UnitActorFixture.Name(method)]);
    }

    // Each way a kept map cannot be taken: the method is analysed instead.
    [Theory]
    [InlineData("no map")]
    [InlineData("anchor type")]
    [InlineData("changed")]
    [InlineData("layout fails")]
    [InlineData("site fails")]
    [InlineData("type key")]
    [InlineData("method key")]
    [InlineData("other kind")]
    [InlineData("unrecorded key")]
    [InlineData("corrupt")]
    public void AKeptMapThatCannotBeTakenIsAnalysedAgain(string reason)
    {
        var method = UnitActorFixture.Method();
        var other = CliTypeIdentity.Named(UnitActorFixture.Assembly, "Tests", "Other", isValueType: false);
        var map = UnitActorFixture.RootMap(method, anchor: reason == "anchor type" ? other : null);
        using var state = UnitActorFixture.State(method);
        static Func<ImmutableArray<UnitDependency>, ImmutableArray<UnitDependency>> Fact(UnitQueryKind kind, string key) =>
            recorded => [.. recorded, new(kind, key, new(1, 1))];
        state.Store = reason switch
        {
            "no map" => Kept(method, map, keepMap: false),
            "type key" => Kept(method, map, Fact(UnitQueryKind.ValueLayout, "no-such-type")),
            "method key" => Kept(method, map, Fact(UnitQueryKind.AllocatingMethod, "not-a-definition")),
            "other kind" => Kept(method, map, Fact(UnitQueryKind.StringLayout, "hello")),
            // A key that names the definition, but not as recording names it.
            "unrecorded key" => Kept(method, map, Fact(UnitQueryKind.AllocatingMethod, "UnitActors:0x6000002")),
            "corrupt" => UnitActorFixture.Corrupt(UnitActorFixture.Unit(state, method, map) with { RootMap = UnitRootMapFormat.Write(map) }),
            _ => Kept(method, map),
        };
        var world = reason switch
        {
            "changed" => new World(Allocates: false),
            "layout fails" => new World(LayoutFailure: new CompilerException(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata, "broken"))),
            "site fails" => new World(SiteFailure: new KeyNotFoundException("site")),
            _ => new World(),
        };
        var ran = new List<string>();
        var recorder = Recorder(state, world);

        recorder.Map(method, reason.EndsWith("fails", StringComparison.Ordinal)
            ? () => { ran.Add("analysed"); return map; }
        : Analysis(recorder, map, ran));

        Assert.Equal(["analysed"], ran);
        Assert.Equal(0, state.RestoredRootMaps);
    }

    // Verification analyses a method whose map is taken: the two must be the same.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnderVerificationATakenMapMustBeTheAnalysedOne(bool same)
    {
        var method = UnitActorFixture.Method();
        var map = UnitActorFixture.RootMap(method);
        using var state = new CompiledUnitState("units")
        {
            Verifies = true,
            Policy = "Wasm32/Speed/Disabled",
            Bodies = CompiledUnitState.Index([method], []),
        };
        state.Store = Kept(method, map);
        var recorder = Recorder(state, new());
        var analysed = same ? map : UnitActorFixture.RootMap(method, safepoint: 2);

        if (same)
        {
            Assert.Equal(UnitFingerprintOf(map), UnitFingerprintOf(recorder.Map(method, Analysis(recorder, analysed))));
        }
        else
        {
            var failure = Assert.Throws<InvalidOperationException>(() => recorder.Map(method, Analysis(recorder, analysed)));
            Assert.Equal($"The compiled unit of {UnitActorFixture.Name(method)} restored a root map that differs from analysis.",
                failure.Message);
        }
    }

    [Fact]
    public void ItNeedsItsStateAndFieldDefinitions()
    {
        Assert.Throws<ArgumentNullException>(() => new UnitRootMapRecorder(null!, new Layouts(null), new Sites(null), new HashSet<EntityKey>(),
            new HashSet<string>(), new NoFields()));
        Assert.Throws<ArgumentNullException>(() => new UnitRootMapRecorder(UnitActorFixture.State(), new Layouts(null), new Sites(null),
            new HashSet<EntityKey>(), new HashSet<string>(), null!));
    }

    private static UnitFingerprint UnitFingerprintOf(MethodRootMap map)
    {
        var fingerprint = new UnitFingerprintBuilder();
        fingerprint.Add(map);
        return fingerprint.Finish();
    }

    private sealed class Layouts(Exception? failure) : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) => failure is null ? new(type, 8, 4, [0]) : throw failure;
    }

    // The analysis asks a site by key alone.
    private sealed class Sites(Exception? failure) : IReadOnlyDictionary<string, DispatchCallSiteModel>
    {
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out DispatchCallSiteModel value)
        {
            value = null;
            return failure is null ? false : throw failure;
        }

        public DispatchCallSiteModel this[string key] => throw new NotSupportedException();
        public IEnumerable<string> Keys => throw new NotSupportedException();
        public IEnumerable<DispatchCallSiteModel> Values => throw new NotSupportedException();
        public int Count => throw new NotSupportedException();
        public bool ContainsKey(string key) => throw new NotSupportedException();
        public IEnumerator<KeyValuePair<string, DispatchCallSiteModel>> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class NoFields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();
    }
}
