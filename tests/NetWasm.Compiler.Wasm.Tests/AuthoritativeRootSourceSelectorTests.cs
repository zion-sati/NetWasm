// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AuthoritativeRootSourceSelectorTests
{
    private static AuthoritativeRootSourceSelector Create() => new(
        new RootClearElisionPlanner(new StructuredBlockPredecessorReader()),
        new RootValueElisionPlanner(new StructuredBlockPredecessorReader()));

    [Fact]
    public void RejectsMissingInputs()
    {
        var planner = new RootClearElisionPlanner(new StructuredBlockPredecessorReader());
        var values = new RootValueElisionPlanner(new StructuredBlockPredecessorReader());
        Assert.Throws<ArgumentNullException>(() => new AuthoritativeRootSourceSelector(null!, values));
        Assert.Throws<ArgumentNullException>(() => new AuthoritativeRootSourceSelector(planner, null!));
        var arrays = new ArrayMetadataCachingPlan([], []);
        Assert.Throws<ArgumentNullException>(() => Create().Select(null!, CreateMethodEmissionContext(), arrays));
        Assert.Throws<ArgumentNullException>(() => Create().Select(StoreThenLoop(), null!, arrays));
        Assert.Throws<ArgumentNullException>(() => Create().Select(StoreThenLoop(), CreateMethodEmissionContext(), null!));
    }

    [Fact]
    public void ExistingAddressableAndCachedStorageIsNotRedirected()
    {
        var selected = new RootSource(RootSourceKind.Argument, 0);
        var local = new RootSource(RootSourceKind.Local, 0);
        var spilled = new RootSource(RootSourceKind.Local, 1);
        var argument = new RootSource(RootSourceKind.Argument, 1);
        var cached = new RootSource(RootSourceKind.Local, 2);
        var captured = new RootSource(RootSourceKind.Local, 3);
        var indirect = new RootSource(RootSourceKind.Local, 4, 0);
        var temporary = new RootSource(RootSourceKind.EvaluationStack, 0);
        var all = new[] { selected, local, spilled, argument, cached, captured, indirect, temporary };
        var roots = new MethodRootMap(EntryKey,
            all.Select((source, slot) => (source, slot)).ToImmutableDictionary(x => x.source, x => x.slot),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(2, new(2, [.. all], [])));
        var values = new ValueFrameLayout(16, [], ImmutableDictionary<int, int>.Empty.Add(1, 0), [], [1]);
        var captureSlot = new CapturedSlot(false, 3);
        var filters = new FilterEnvironmentLayout(16, 1, 0,
            ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(captureSlot,
                new(captureSlot, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), 0, [])));
        var selector = new[] { Create() }.Cast<IAuthoritativeRootSourceSelector>().Single();

        var result = selector.Select(StoreThenLoop(), Context(roots) with { ValueLayout = values, FilterEnvironment = filters },
            new ArrayMetadataCachingPlan([new(false, 2)], []));

        Assert.Equal(2, result.Count);
        Assert.Contains(selected, result);
        Assert.Contains(local, result);
    }

    // A byref argument keeps one value for the call unless stored, so its slot is written at entry
    // and on a store instead of at every safepoint; one held in the value frame or captured by a
    // filter is not.
    [Fact]
    public void ByrefArgumentsAreAuthoritativeUnlessFramedOrCaptured()
    {
        var receiver = new RootSource(RootSourceKind.ManagedAddressArgument, 0);
        var framed = new RootSource(RootSourceKind.ManagedAddressArgument, 1);
        var captured = new RootSource(RootSourceKind.ManagedAddressArgument, 2);
        var byrefLocal = new RootSource(RootSourceKind.ManagedAddressLocal, 0);
        var roots = new MethodRootMap(EntryKey,
            new[] { receiver, framed, captured, byrefLocal }
                .Select((source, slot) => (source, slot)).ToImmutableDictionary(x => x.source, x => x.slot), []);
        var values = new ValueFrameLayout(16, [], ImmutableDictionary<int, int>.Empty.Add(1, 0), [], []);
        var captureSlot = new CapturedSlot(true, 2);
        var filters = new FilterEnvironmentLayout(16, 1, 0,
            ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(captureSlot,
                new(captureSlot, CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress), 0, [])));

        var result = Create().Select(StoreThenLoop(), Context(roots) with { ValueLayout = values, FilterEnvironment = filters },
            new ArrayMetadataCachingPlan([], []));

        Assert.Equal([receiver], result);
    }

    // Local 0 is stored once, outside the loop. Published once outside it, the store and the publication tie, and it is
    // published; inside the loop the publication runs every turn (the value planner cannot carry it around the back
    // edge), so its slot is written at the store instead; a constructor call's publication counts the same; one a
    // throw-only point leaves unpublished while its slot holds null counts nothing.
    [Theory]
    [InlineData(1, false, false, false)]
    [InlineData(1, true, false, false)]
    [InlineData(1, false, true, false)]
    [InlineData(2, false, false, true)]
    [InlineData(2, true, false, true)]
    [InlineData(2, false, true, false)]
    public void AVariableIsAuthoritativeWhereItsStoresCostLessThanItsPublications(int offset, bool constructor, bool deferrable,
        bool authoritative)
    {
        var local = new RootSource(RootSourceKind.Local, 0);
        var point = constructor
            ? new SafepointRootMap(offset, [], [local])
            : new SafepointRootMap(offset, [local], []) { Deferrable = deferrable ? [local] : [] };
        var roots = new MethodRootMap(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(local, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(offset, point));

        var result = Create().Select(StoreThenLoop(), Context(roots), new ArrayMetadataCachingPlan([], []));

        Assert.Equal(authoritative, result.Contains(local));
    }

    // An argument's slot is also written on entry, so one publication does not pay for it but two do.
    [Fact]
    public void AnArgumentsEntryWriteCountsAsAStore()
    {
        var argument = new RootSource(RootSourceKind.Argument, 0);
        MethodRootMap Roots(params int[] offsets) => new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(argument, 0),
            offsets.ToImmutableDictionary(offset => offset, offset => new SafepointRootMap(offset, [], [argument])));

        Assert.Empty(Create().Select(StoreThenLoop(), Context(Roots(1)), new ArrayMetadataCachingPlan([], [])));
        Assert.Equal([argument], Create().Select(StoreThenLoop(), Context(Roots(0, 1)), new ArrayMetadataCachingPlan([], [])));
    }

    private static MethodEmissionContext Context(MethodRootMap roots) => CreateMethodEmissionContext() with { RootMap = roots };

    // Block 0 stores local 0 at offset 0 and calls at 1; block 1 calls at 2 and branches back to itself at 3.
    private static StructuredMethod StoreThenLoop()
    {
        var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
            .Add(new(0), new(new(0), 0, [I(0, CilOperation.StoreLocal, new CilOperand.Index(0)), I(1, CilOperation.Call)], [],
                new StructuredFallthroughExit(new(1))) { EndOffset = 2 })
            .Add(new(1), new(new(1), 2, [I(2, CilOperation.Call)], [], new StructuredBranchExit(3, new(1))) { EndOffset = 3 });
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1);
        return new(new(definition, null, 1, [], [], []), new(0), blocks, StructuredSequence.Empty, [], [], []);
    }
}
