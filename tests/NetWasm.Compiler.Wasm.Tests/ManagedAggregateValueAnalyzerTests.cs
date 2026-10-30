using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedAggregateValueAnalyzerTests
{
    [Theory]
    [InlineData(CilOperation.Return)]
    [InlineData(CilOperation.StoreStaticField)]
    [InlineData(CilOperation.StoreArrayElement)]
    [InlineData(CilOperation.StoreArrayElementReference)]
    [InlineData(CilOperation.StoreRectangularArrayElement)]
    [InlineData(CilOperation.CallIndirect)]
    [InlineData(CilOperation.CopyObject)]
    [InlineData(CilOperation.InitializeObject)]
    [InlineData(CilOperation.CopyBlock)]
    [InlineData(CilOperation.InitializeBlock)]
    [InlineData(CilOperation.CompareExchange)]
    public void AddressConsumingOperationsRejectOwnedIdentity(CilOperation operation)
    {
        // Below the References level, initobj consumes the address too.
        var fixture = new Fixture() { References = false };

        Assert.Empty(fixture.Analyze(fixture.AddressConsumerMethod(operation)).Storages);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnknownAddressProducersDoNotAliasOwnedStorage(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var analysis = fixture.Analyze(fixture.UnknownAddressProducerMethod());

        Assert.Single(analysis.Storages);
        Assert.Single(analysis.LaneAccesses);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RecordsManagedAddressLocalRootDependencies(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var method = fixture.LoopMethod();
        var source = new RootSource(RootSourceKind.ManagedAddressLocal, 0);
        var rootMap = new MethodRootMap(
            fixture.Body.Definition.Key,
            ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                10, new(10, [source], [])));

        var dependency = Assert.Single(fixture.Analyze(method, rootMap).RootDependencies);

        Assert.Equal(source, dependency.Source);
        Assert.Equal(ManagedAggregateRootSetKind.Ordinary, dependency.Set);
        Assert.Equal(ManagedAggregateStorageKind.Argument, dependency.Address.Storage.Kind);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void IgnoresNonAggregateLocalAddresses(WasmTarget target)
    {
        var fixture = new Fixture(target);

        Assert.Same(ManagedAggregateValueAnalysis.Empty,
            fixture.Analyze(fixture.NonAggregateLocalAddressMethod()));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MissingStorageOriginsAndNonScalarObjectAccessRemainUnpromoted(WasmTarget target)
    {
        var fixture = new Fixture(target);

        var missingOrigin = fixture.Analyze(fixture.MixedLocalAddressMethod());
        Assert.Single(missingOrigin.Storages);
        Assert.Single(missingOrigin.LaneAccesses);
        Assert.Empty(fixture.Analyze(fixture.NonScalarObjectAccessMethod()).Storages);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FallthroughSuccessorEntryStackDefinesLastInstructionOutput(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var analysis = fixture.Analyze(fixture.FallthroughAddressMethod());

        Assert.Single(analysis.Storages);
        Assert.Single(analysis.LaneAccesses);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnknownAndUnresolvedFieldAddressesDoNotAcquireOwnedIdentity(WasmTarget target)
    {
        var fixture = new Fixture(target);

        Assert.Single(fixture.Analyze(fixture.ExternalFieldAddressMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.UnresolvedFieldAddressMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.MismatchedObjectAccessMethod()).Storages);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AddressStateMergeIsOrderIndependent(WasmTarget target)
    {
        var fixture = new Fixture(target);

        Assert.Empty(fixture.Analyze(fixture.AmbiguousMergeMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.AmbiguousMergeMethod(swapBranches: true)).Storages);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void StructuredLeaveAndTerminalExitsPreserveOwnedAccesses(
        WasmTarget target, bool terminal)
    {
        var fixture = new Fixture(target);
        var analysis = fixture.Analyze(fixture.StructuredExitMethod(terminal));

        Assert.Single(analysis.Storages);
        Assert.Single(analysis.LaneAccesses);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RejectsStructuredExitsWhoseSuccessorRequiresMoreStack(WasmTarget target)
    {
        var fixture = new Fixture(target);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Analyze(fixture.InvalidExitStackMethod()));

        Assert.Equal("Aggregate value analysis encountered an invalid structured exit stack.",
            exception.Message);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DuplicateAndIndirectCallsCannotPreserveOwnedAddressIdentity(WasmTarget target)
    {
        var fixture = new Fixture(target);

        Assert.Empty(fixture.Analyze(fixture.DuplicateAddressMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.IndirectAddressCallMethod()).Storages);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4, true)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4, true)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, true)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8, true)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedReference, false)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedReference, false)]
    public void UnsafeAliasViewsAcceptOnlyInBoundsScalarStorage(
        WasmTarget target, CliValueKind viewKind, bool expected)
    {
        var fixture = new Fixture(target);
        var method = fixture.UnsafeAliasViewMethod(viewKind);

        var analysis = fixture.Analyze(method);

        Assert.Equal(expected, !analysis.Storages.IsEmpty);
        if (expected)
        {
            Assert.Equal(viewKind,
                Assert.Single(analysis.LaneAccesses.Values).Address.Type.StackKind);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void RejectsAddressPassingCallsWithoutResolvedMethodEffects(WasmTarget target, bool entityOperand)
    {
        var fixture = new Fixture(target);
        var method = fixture.LoopMethod();
        if (entityOperand)
        {
            method = RewriteInstructions(method, instruction =>
                instruction.Operation == CilOperation.Call
                    ? instruction with { Operand = new CilOperand.Entity(fixture.Target.Definition.Key) }
                    : instruction);
        }
        var request = fixture.Request(method, fixture.EmptyRootMap());
        request = request with
        {
            ByRefEffects = new(request.ByRefEffects.Methods.Remove(fixture.Target.CanonicalName)),
        };

        Assert.Same(ManagedAggregateValueAnalysis.Empty, fixture.Analyzer.Analyze(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, -1)]
    [InlineData(WasmTarget.Wasm64, -1)]
    [InlineData(WasmTarget.Wasm32, 8)]
    [InlineData(WasmTarget.Wasm64, 8)]
    public void RejectsFieldLoadsAndFieldAddressesOutsideOwnedStorage(WasmTarget target, int offset)
    {
        var fixture = new Fixture(target) { LowFieldOffset = offset };

        Assert.Empty(fixture.Analyze(fixture.WholeValueLoadMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.ObjectAccessMethod()).Storages);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ResolvesEntityFieldOperandsAgainstTheirDeclaringType(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var method = RewriteInstructions(fixture.WholeValueLoadMethod(), instruction =>
            instruction.Operand is CilOperand.FieldInstance field
                ? instruction with { Operand = new CilOperand.Entity(field.Value.Definition.Key) }
                : instruction);

        var analysis = fixture.Analyze(method);

        Assert.Single(analysis.Storages);
        var access = Assert.Single(analysis.LaneAccesses.Values);
        Assert.Equal(1, access.InstructionOffset);
        Assert.Equal(0, access.Address.ByteOffset);
        Assert.False(access.Writes);
        Assert.Single(analysis.Materializations);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm64, 0)]
    [InlineData(WasmTarget.Wasm32, 2)]
    [InlineData(WasmTarget.Wasm64, 2)]
    public void RejectsStackFactsThatDisagreeWithInstructionConsumption(WasmTarget target, int outgoingCount)
    {
        var fixture = new Fixture(target);
        var method = fixture.WholeValueLoadMethod();
        method = method with
        {
            InstructionEntryStacks = method.InstructionEntryStacks.SetItem(1,
                [.. Enumerable.Repeat(CliValueKind.ManagedAddress, outgoingCount)]),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.Analyze(method));

        Assert.StartsWith("Aggregate value analysis disagrees with the validated CIL stack", exception.Message);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TracksLocalLanesAndWholeValueMaterializationButRejectsWholeValueStores(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var analysis = fixture.Analyze(fixture.LocalValueMethod(storeWholeValue: false));

        var storage = Assert.Single(analysis.Storages.Values);
        Assert.Equal(ManagedAggregateStorageKind.Local, storage.Id.Kind);
        Assert.Equal(0, storage.Id.Index);
        var access = Assert.Single(analysis.LaneAccesses.Values);
        Assert.Equal(storage.Id, access.Address.Storage);
        Assert.False(access.Writes);
        var materialization = Assert.Single(analysis.Materializations);
        Assert.Equal(3, materialization.InstructionOffset);
        Assert.Equal(storage.Id, materialization.Storage);
        Assert.False(materialization.ReloadAfter);
        // The store is in no loop the lanes are read in, so it would only add a
        // flush and a reload.
        Assert.Empty(fixture.Analyze(fixture.LocalValueMethod(storeWholeValue: true)).Storages);
    }

    // An enumerator assigned before the loop that moves it: the lanes reload
    // once after the store and serve every iteration.
    [Fact]
    public void WholeValueStoresOutsideTheLoopThatReadsTheLanesReloadThem()
    {
        var fixture = new Fixture();

        var analysis = fixture.Analyze(fixture.StoreThenLoopMethod(storeInLoop: false));

        Assert.Single(analysis.Storages);
        Assert.Contains(analysis.Materializations, item => item is { InstructionOffset: 1, ReloadAfter: true });
        Assert.Empty(fixture.Analyze(fixture.StoreThenLoopMethod(storeInLoop: true)).Storages);
    }

    [Fact]
    public void ExcludedLocalsAreNoStorage()
    {
        var fixture = new Fixture();
        var method = fixture.LocalValueMethod(storeWholeValue: true);

        Assert.Same(ManagedAggregateValueAnalysis.Empty, fixture.Analyzer.Analyze(
            fixture.Request(method, fixture.EmptyRootMap()) with { ExcludedLocals = [0] }));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 4)]
    [InlineData(WasmTarget.Wasm64, 4)]
    [InlineData(WasmTarget.Wasm32, 20)]
    [InlineData(WasmTarget.Wasm64, 20)]
    [InlineData(WasmTarget.Wasm32, 10)]
    [InlineData(WasmTarget.Wasm64, 10)]
    public void DeclinesStorageOutsideTheLaneSizeContract(WasmTarget target, int size)
    {
        var fixture = new Fixture(target);
        fixture.AggregateLayout = new(fixture.Aggregate, size, 2, []);

        Assert.Same(ManagedAggregateValueAnalysis.Empty,
            fixture.Analyze(fixture.LocalValueMethod(storeWholeValue: false)));
        Assert.Same(ManagedAggregateValueAnalysis.Empty,
            fixture.Analyze(fixture.WholeValueLoadMethod()));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DeclinesReferenceLanesBelowTheReferencesLevel(WasmTarget target)
    {
        var fixture = new Fixture(target) { References = false };
        fixture.AggregateLayout = new(fixture.Aggregate, 16, 8, [0]);
        fixture.LowFieldType = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);

        Assert.Same(ManagedAggregateValueAnalysis.Empty, fixture.Analyze(fixture.FieldRoundTripMethod()));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 16, 2)]
    [InlineData(WasmTarget.Wasm64, 16, 4)]
    [InlineData(WasmTarget.Wasm64, 16, 12)]
    [InlineData(WasmTarget.Wasm64, 12, 8)]
    public void DeclinesReferencesThatDoNotFillWholeLanes(WasmTarget target, int size, int offset)
    {
        var fixture = new Fixture(target);
        fixture.AggregateLayout = new(fixture.Aggregate, size, 8, [offset]);

        Assert.Same(ManagedAggregateValueAnalysis.Empty,
            fixture.Analyze(fixture.LocalValueMethod(storeWholeValue: false)));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, false, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, true, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, true, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, false, CliValueKind.I8)]
    public void DeclinesIntegerAccessesThatOverlapAReferenceLane(WasmTarget target, bool byReference, CliValueKind kind)
    {
        var fixture = new Fixture(target);
        fixture.AggregateLayout = new(fixture.Aggregate, 16, 8, [0])
        {
            ByReferenceOffsets = byReference ? [0] : [],
        };
        fixture.LowFieldType = CliTypeIdentity.FromStackKind(kind);

        Assert.Empty(fixture.Analyze(fixture.FieldRoundTripMethod()).Storages);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void PromotesReferenceAndByReferenceFieldsToTheirOwnLanes(WasmTarget target, bool byReference)
    {
        var fixture = new Fixture(target);
        var pointer = WasmTargetLayout.For(target).ObjectReferenceSize;
        var kind = byReference ? CliValueKind.ManagedAddress : CliValueKind.ManagedReference;
        fixture.AggregateLayout = new(fixture.Aggregate, 16, 8, [0])
        {
            ByReferenceOffsets = byReference ? [0] : [],
        };
        fixture.LowFieldType = CliTypeIdentity.FromStackKind(kind);
        var method = fixture.FieldRoundTripMethod();

        var analysis = fixture.Analyze(method);

        var storage = Assert.Single(analysis.Storages).Value;
        Assert.Equal(new ManagedAggregateLaneShape(0, pointer, kind), storage.Lanes.First());
        Assert.Equal(16, storage.Lanes.Sum(lane => lane.Size));
        Assert.Equal([0], analysis.LaneAccesses[2].LaneOffsets.ToArray());
        Assert.Equal([0], analysis.LaneAccesses[4].LaneOffsets.ToArray());
        var plan = new ManagedAggregateSsaPlanner().Plan(new(method, analysis));
        Assert.Equal(storage.Lanes.Count(), plan.Lanes.Length);
        Assert.Equal(Assert.Single(plan.Accesses[2].Writes).Value, Assert.Single(plan.Accesses[4].Reads).Value);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InitializeObjectClearsEveryLane(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var method = fixture.ClearMethod(fixture.Aggregate);

        var analysis = fixture.Analyze(method);

        var clear = analysis.LaneAccesses[1];
        Assert.True(clear.Writes);
        Assert.Equal(CliValueKind.ValueType, clear.StackKind);
        Assert.Equal([0, 4], clear.LaneOffsets.ToArray());
        var plan = new ManagedAggregateSsaPlanner().Plan(new(method, analysis));
        Assert.Equal(2, plan.Accesses[1].Writes.Length);
        Assert.Contains(Assert.Single(plan.Accesses[3].Reads).Value,
            plan.Accesses[1].Writes.Select(write => write.Value));
    }

    [Fact]
    public void InitializeObjectOfAnotherTypeRejectsTheStorage()
    {
        var fixture = new Fixture();

        Assert.Empty(fixture.Analyze(fixture.ClearMethod(fixture.View)).Storages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializeObjectOfAFieldClearsItsLane(bool high)
    {
        var fixture = new Fixture();

        var analysis = fixture.Analyze(fixture.FieldClearMethod(high ? fixture.High : fixture.Low));

        Assert.Single(analysis.Storages);
        var clear = analysis.LaneAccesses[2];
        Assert.True(clear.Writes);
        Assert.Equal(high ? 4 : 0, Assert.Single(clear.LaneOffsets));
    }

    [Fact]
    public void InitializeObjectThroughAnUnknownAddressLeavesOwnedStorage()
    {
        var fixture = new Fixture();
        var analysis = fixture.Analyze(fixture.UnknownClearMethod());

        Assert.Single(analysis.Storages);
        Assert.DoesNotContain(analysis.LaneAccesses.Values, access => access.StackKind == CliValueKind.ValueType);
    }

    [Fact]
    public void WritingCallReloadsReachTheNextLoopIteration()
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var analysis = fixture.Analyze(method, targetEffects: new([0], [], []));
        var plan = new ManagedAggregateSsaPlanner().Plan(new(method, analysis));

        var synchronization = Assert.Single(plan.Materializations);
        Assert.True(synchronization.Materialization.ReloadAfter);
        Assert.Equal(2, synchronization.Reloads.Length);
        foreach (var reloaded in synchronization.Reloads)
        {
            Assert.Contains(plan.Definitions, definition =>
                definition.Value == reloaded.Value &&
                definition.Kind == ManagedAggregateSsaDefinitionKind.Reload &&
                definition.InstructionOffset == 10);
            var phi = Assert.Single(plan.Phis, phi => phi.Lane == reloaded.Lane);
            Assert.Equal(reloaded.Value, phi.Incoming[new StructuredBlockId(1)]);
            Assert.NotEqual(synchronization.Values.Single(value =>
                value.Lane == reloaded.Lane).Value, reloaded.Value);
        }
        Assert.Equal(plan.Phis.Single(phi => phi.Lane.ByteOffset == 0).Result,
            Assert.Single(plan.Accesses[4].Reads).Value);
    }

    [Fact]
    public void RepeatedCallArgumentsMergeReadOnlyAndWritingObligations()
    {
        var fixture = new Fixture();
        var method = fixture.DuplicateReadOnlyCallMethod();
        var request = fixture.Request(method, fixture.EmptyRootMap());
        request = request with
        {
            ByRefEffects = new(request.ByRefEffects.Methods.SetItem(
                fixture.MultiTarget.CanonicalName, new([1], [], []))),
        };
        var analysis = fixture.Analyzer.Analyze(request);
        var synchronization = Assert.Single(analysis.Materializations);
        Assert.True(synchronization.ReloadAfter);
        var plan = new ManagedAggregateSsaPlanner().Plan(new(method, analysis));
        Assert.Single(plan.Storages);
        Assert.Equal(2, Assert.Single(plan.Materializations).Reloads.Length);
    }

    [Fact]
    public void BuildsPrunedLoopPhiAndMaterializationValues()
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var managedAddress = new RootSource(
            RootSourceKind.ManagedAddressEvaluationStack,
            0);
        var rootMap = new MethodRootMap(
            fixture.Body.Definition.Key,
            ImmutableDictionary<RootSource, int>.Empty.Add(managedAddress, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                10,
                new(10, [managedAddress], [managedAddress])));
        var analysis = fixture.Analyze(method, rootMap);

        var plan = new ManagedAggregateSsaPlanner().Plan(new(method, analysis));

        Assert.Single(plan.Storages);
        Assert.Equal(2, plan.Lanes.Length);
        Assert.Equal(4, plan.Definitions.Length);
        var phi = Assert.Single(plan.Phis);
        Assert.Equal(new StructuredBlockId(1), phi.Block);
        Assert.Equal(0, phi.Lane.ByteOffset);
        Assert.Equal(2, phi.Incoming.Count);
        Assert.Equal(2, plan.EdgeCopies.Length);
        Assert.Equal(phi.Result, plan.Accesses[4].Reads.Single().Value);
        Assert.Equal(analysis.LaneAccesses[4], plan.Accesses[4].Access);
        var stored = plan.Accesses[8].Writes.Single().Value;
        var storeDefinition = Assert.Single(plan.Definitions, definition => definition.Value == stored);
        Assert.Equal(ManagedAggregateSsaDefinitionKind.Store, storeDefinition.Kind);
        Assert.Equal(plan.Accesses[8].Writes.Single().Lane, storeDefinition.Lane);
        Assert.Equal(new StructuredBlockId(1), storeDefinition.Block);
        var phiDefinition = Assert.Single(plan.Definitions, definition => definition.Value == phi.Result);
        Assert.Equal(ManagedAggregateSsaDefinitionKind.Phi, phiDefinition.Kind);
        Assert.Equal(phi.Lane, phiDefinition.Lane);
        Assert.Equal(phi.Block, phiDefinition.Block);
        var materialization = Assert.Single(plan.Materializations);
        Assert.Contains(materialization.Values, value =>
            value.Lane.ByteOffset == 0 && value.Value == stored);
        Assert.Contains(materialization.Values, value =>
            value.Lane.ByteOffset == 4 &&
            value.Value == plan.InitialValues[value.Lane]);
        Assert.Equal(2, plan.RootDependencies.Length);
    }

    [Fact]
    public void SsaPlannerDeclinesAControlFlowBackedgeToMethodEntry()
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var analysis = fixture.Analyze(method);
        var exit = new StructuredBlockId(2);
        method = method with
        {
            Blocks = method.Blocks.SetItem(exit, method.Blocks[exit] with
            { Exit = new StructuredBranchExit(14, method.EntryBlock) })
        };

        Assert.Same(ManagedAggregateSsaPlan.Empty, new ManagedAggregateSsaPlanner().Plan(new(method, analysis)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SsaPlannerPreservesLoopValuesAcrossFallthroughAndLeaveEdges(bool leave)
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var analysis = fixture.Analyze(method);
        var entry = method.EntryBlock;
        method = method with
        {
            Blocks = method.Blocks.SetItem(entry, method.Blocks[entry] with
            {
                Exit = leave ? new StructuredLeaveExit(2, new(1)) : new StructuredFallthroughExit(new(1)),
            }),
        };

        var plan = new ManagedAggregateSsaPlanner().Plan(new(method, analysis));

        Assert.Single(plan.Storages);
        var phi = Assert.Single(plan.Phis);
        Assert.Equal(plan.InitialValues[phi.Lane], phi.Incoming[entry]);
        Assert.Equal(plan.Accesses[8].Writes.Single().Value, phi.Incoming[new(1)]);
        Assert.Equal(phi.Result, plan.Accesses[4].Reads.Single().Value);
    }

    [Fact]
    public void SsaPlannerRejectsMaterializationOutsideReachableInstructions()
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var analysis = fixture.Analyze(method);
        var original = Assert.Single(analysis.Materializations);
        analysis = analysis with { Materializations = [original with { InstructionOffset = 999 }] };

        Assert.Same(ManagedAggregateSsaPlan.Empty, new ManagedAggregateSsaPlanner().Plan(new(method, analysis)));
    }

    [Fact]
    public void SsaPlannerConvergesWhenPredecessorsHaveLaterBlockIds()
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var first = method.Blocks[new(0)] with { Id = new(2) };
        var loop = method.Blocks[new(1)];
        loop = loop with { Exit = ((StructuredConditionalExit)loop.Exit) with { WhenNotTaken = new(0) } };
        var exit = method.Blocks[new(2)] with { Id = new(0) };
        method = method with
        {
            EntryBlock = first.Id,
            Blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(first.Id, first).Add(loop.Id, loop).Add(exit.Id, exit),
        };
        var analysis = fixture.Analyze(method);

        var plan = new ManagedAggregateSsaPlanner().Plan(new(method, analysis));

        var phi = Assert.Single(plan.Phis);
        Assert.Equal(plan.InitialValues[phi.Lane], phi.Incoming[first.Id]);
        Assert.Equal(plan.Accesses[8].Writes.Single().Value, phi.Incoming[loop.Id]);
        Assert.Equal(phi.Result, plan.Accesses[4].Reads.Single().Value);
        var materialization = Assert.Single(plan.Materializations);
        Assert.Equal(plan.Accesses[8].Writes.Single().Value,
            Assert.Single(materialization.Values, value => value.Lane.ByteOffset == 0).Value);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void SsaPlannerKeepsMaterializationsAndReloadsBoundToTheirOwnStorage(WasmTarget target, bool writingCall)
    {
        var fixture = new Fixture(target);
        var method = fixture.TwoOwnedStoragesMethod();
        var analysis = fixture.Analyze(method, targetEffects: new(writingCall ? [0] : [], [], []));

        var plan = new ManagedAggregateSsaPlanner().Plan(new(method, analysis));

        Assert.Equal(writingCall ? 2 : 1, plan.Storages.Count);
        Assert.Equal(writingCall ? 2 : 1, plan.Materializations.Length);
        Assert.All(plan.Materializations, materialization =>
        {
            Assert.Equal(2, materialization.Values.Length);
            Assert.Equal(writingCall ? 2 : 0, materialization.Reloads.Length);
            Assert.All(materialization.Values, value =>
                Assert.Equal(materialization.Materialization.Storage, value.Lane.Storage));
            Assert.All(materialization.Reloads, value =>
                Assert.Equal(materialization.Materialization.Storage, value.Lane.Storage));
        });
        Assert.Equal(writingCall ? 2 : 1, plan.Materializations.Select(item => item.Materialization.Storage).Distinct().Count());
    }

    [Fact]
    public void SsaPlannerDeclinesStorageThatIsOnlyWritten()
    {
        var fixture = new Fixture();
        var method = fixture.ClearMethod(fixture.Aggregate);
        var analysis = fixture.Analyze(method);
        analysis = analysis with { LaneAccesses = analysis.LaneAccesses.Remove(3) };

        Assert.Same(ManagedAggregateSsaPlan.Empty,
            new ManagedAggregateSsaPlanner().Plan(new(method, analysis) { References = true }));
        // Below the References level, a written storage is still selected.
        Assert.Single(new ManagedAggregateSsaPlanner().Plan(new(method, analysis)).Storages);
    }

    [Theory]
    [InlineData("alignment")]
    [InlineData("size")]
    [InlineData("kind")]
    [InlineData("lane-kind")]
    public void SsaPlannerDeclinesStorageWithUnsupportedAccessLayout(string invalid)
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var analysis = fixture.Analyze(method);
        var access = analysis.LaneAccesses[4];
        access = invalid switch
        {
            "alignment" => access with { Address = access.Address with { ByteOffset = 2 } },
            "size" => access with { Size = 2 },
            "kind" => access with { StackKind = CliValueKind.F4 },
            "lane-kind" => access with { LaneOffsets = [0] },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        analysis = analysis with { LaneAccesses = analysis.LaneAccesses.SetItem(4, access) };

        Assert.Same(ManagedAggregateSsaPlan.Empty, new ManagedAggregateSsaPlanner().Plan(new(method, analysis)));
    }

    [Fact]
    public void SsaPlannerDeclinesReadOnlyOrRejectedCandidatesAndNullRequests()
    {
        var fixture = new Fixture();
        var planner = new ManagedAggregateSsaPlanner();
        var readOnly = fixture.UnsafeAsMethod();
        var rejected = fixture.AmbiguousMergeMethod();

        Assert.Empty(planner.Plan(new(
            readOnly,
            fixture.Analyze(readOnly))).Storages);
        Assert.Empty(planner.Plan(new(
            rejected,
            fixture.Analyze(rejected))).Storages);
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(new(null!,
            ManagedAggregateValueAnalysis.Empty)));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(new(
            readOnly,
            null!)));
    }

    [Fact]
    public void SsaPlannerTreatsDuplicateConditionalTargetsAsOneCfgEdge()
    {
        var fixture = new Fixture();
        var method = fixture.SameTargetConditionalMethod();

        var plan = new ManagedAggregateSsaPlanner().Plan(new(
            method,
            fixture.Analyze(method)));

        Assert.Single(plan.Storages);
        Assert.Empty(plan.Phis);
        Assert.Empty(plan.EdgeCopies);
    }

    [Fact]
    public void TracksNestedLanesLoopStateMaterializationAndExactRootSets()
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var managedAddress = new RootSource(
            RootSourceKind.ManagedAddressEvaluationStack,
            0);
        var unrelated = new RootSource(RootSourceKind.EvaluationStack, 0);
        var rootMap = new MethodRootMap(
            fixture.Body.Definition.Key,
            ImmutableDictionary<RootSource, int>.Empty
                .Add(managedAddress, 0)
                .Add(unrelated, 1),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                10,
                new(10, [managedAddress, unrelated], [managedAddress])));

        var analysis = fixture.Analyze(method, rootMap);

        var storage = Assert.Single(analysis.Storages.Values);
        Assert.Equal(ManagedAggregateStorageKind.Argument, storage.Id.Kind);
        Assert.Equal(0, storage.Id.Index);
        Assert.Collection(
            analysis.LaneAccesses.Values.OrderBy(access => access.InstructionOffset),
            access =>
            {
                Assert.False(access.Writes);
                Assert.Equal(0, access.Address.ByteOffset);
            },
            access =>
            {
                Assert.True(access.Writes);
                Assert.Equal(0, access.Address.ByteOffset);
            });
        Assert.Contains(
            storage.Id,
            analysis.LiveInAddresses[new StructuredBlockId(1)]);
        Assert.Single(analysis.Materializations);
        Assert.Collection(
            analysis.RootDependencies.OrderBy(dependency => dependency.Set),
            dependency =>
            {
                Assert.Equal(ManagedAggregateRootSetKind.Ordinary, dependency.Set);
                Assert.Equal(managedAddress, dependency.Source);
            },
            dependency =>
            {
                Assert.Equal(ManagedAggregateRootSetKind.Constructor, dependency.Set);
                Assert.Equal(managedAddress, dependency.Source);
            });
    }

    [Fact]
    public void RetagsZeroOffsetUnsafeAliasBeforeResolvingNestedField()
    {
        var fixture = new Fixture();
        var analysis = fixture.Analyze(fixture.UnsafeAsMethod());

        var access = Assert.Single(analysis.LaneAccesses.Values);
        Assert.Equal(4, access.Address.ByteOffset);
        Assert.Equal(CliValueKind.I4, access.Address.Type.StackKind);
    }

    [Fact]
    public void TracksFieldAddressesConsumedByScalarObjectLoadsAndStores()
    {
        var fixture = new Fixture();
        var analysis = fixture.Analyze(fixture.ObjectAccessMethod());

        Assert.Collection(
            analysis.LaneAccesses.Values.OrderBy(access => access.InstructionOffset),
            access => Assert.False(access.Writes),
            access => Assert.True(access.Writes));
    }

    [Fact]
    public void MaterializesWholeValueLoadsAndRejectsWholeValueStores()
    {
        var fixture = new Fixture();

        var load = fixture.Analyze(fixture.WholeValueLoadMethod());
        var materialization = Assert.Single(load.Materializations);
        Assert.Equal(3, materialization.InstructionOffset);
        Assert.Equal(ManagedAggregateStorageKind.Argument,
            materialization.Storage.Kind);
        Assert.Equal(0, materialization.Storage.Index);

        Assert.Empty(fixture.Analyze(fixture.WholeValueStoreMethod()).Storages);
    }

    [Fact]
    public void RejectsAmbiguousAddressMergeAndEscapingCall()
    {
        var fixture = new Fixture();

        Assert.Empty(fixture.Analyze(fixture.AmbiguousMergeMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.OffsetAliasMethod()).Storages);
        Assert.Empty(fixture.Analyze(
            fixture.LoopMethod(),
            targetEffects: new([], [0], [])).Storages);
    }

    [Fact]
    public void RejectsInexactManagedByRefReturnAliases()
    {
        var fixture = new Fixture();

        Assert.Empty(fixture.Analyze(
            fixture.ReturnedAliasMethod(),
            targetEffects: new([], [], [0])).Storages);
    }

    [Fact]
    public void SupportsOnlyDirectCallsAndDeduplicatesStorageSynchronization()
    {
        var fixture = new Fixture();

        Assert.Empty(fixture.Analyze(fixture.CallVirtualMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.NewObjectMethod()).Storages);

        var direct = fixture.Analyze(fixture.DuplicateReadOnlyCallMethod());
        var materialization = Assert.Single(direct.Materializations);
        Assert.Equal(2, materialization.InstructionOffset);
        Assert.Equal(new ManagedAggregateStorageId(
            ManagedAggregateStorageKind.Argument,
            0), materialization.Storage);
    }

    [Fact]
    public void RejectsInvalidUnsafeViewsAndUnresolvedFieldWrites()
    {
        var fixture = new Fixture();

        Assert.Empty(fixture.Analyze(fixture.UnsafeBadArityMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.UnsafeOversizedViewMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.UnsafeReferenceViewMethod()).Storages);
        Assert.Empty(fixture.Analyze(fixture.UnresolvedFieldWriteMethod()).Storages);
    }

    [Theory]
    [InlineData(RootSourceKind.ManagedAddressEvaluationStack)]
    [InlineData(RootSourceKind.EvaluationStack)]
    public void RejectsIndirectCandidateRootsAndMismatchedRootMaps(
        RootSourceKind rootKind)
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var indirect = new RootSource(
            rootKind,
            0,
            0);
        var rootMap = new MethodRootMap(
            fixture.Body.Definition.Key,
            ImmutableDictionary<RootSource, int>.Empty.Add(indirect, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                10,
                new(10, [indirect], [])));

        Assert.Empty(fixture.Analyze(method, rootMap).Storages);
        Assert.Throws<InvalidOperationException>(() => fixture.Analyze(
            method,
            rootMap with { Method = new EntityKey(new("other"), 1) }));
    }

    [Fact]
    public void DeclinesDisabledExceptionalOrAnchoredMethods()
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var group = new StructuredExceptionGroupId(0);
        var exceptional = method with
        {
            ExceptionGroups = method.ExceptionGroups.Add(
                group,
                new(group, null, 0, 13, [], [], [], null, null)),
        };
        var anchoredRoots = fixture.EmptyRootMap() with
        {
            AnchorPlan = new(
                [new(
                    new(StackStorageLocationKind.Local, 0),
                    CliTypeIdentity.FromStackKind(CliValueKind.I4))],
                ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty),
        };

        Assert.Empty(fixture.Analyze(method, enabled: false).Storages);
        Assert.Empty(fixture.Analyze(exceptional).Storages);
        Assert.Empty(fixture.Analyze(method, anchoredRoots).Storages);
    }

    [Fact]
    public void RejectsNullDependenciesAndRequestMembers()
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var request = fixture.Request(method, fixture.EmptyRootMap());

        var constructors = new (string Parameter, Func<ManagedAggregateValueAnalyzer> Create)[]
        {
            ("fieldRepository", () => new(null!, fixture, fixture, fixture, fixture, fixture, fixture, fixture, fixture)),
            ("methodRepository", () => new(fixture, null!, fixture, fixture, fixture, fixture, fixture, fixture, fixture)),
            ("typeDefinitions", () => new(fixture, fixture, null!, fixture, fixture, fixture, fixture, fixture, fixture)),
            ("values", () => new(fixture, fixture, fixture, null!, fixture, fixture, fixture, fixture, fixture)),
            ("fields", () => new(fixture, fixture, fixture, fixture, null!, fixture, fixture, fixture, fixture)),
            ("argumentTypes", () => new(fixture, fixture, fixture, fixture, fixture, null!, fixture, fixture, fixture)),
            ("typeOperands", () => new(fixture, fixture, fixture, fixture, fixture, fixture, null!, fixture, fixture)),
            ("layouts", () => new(fixture, fixture, fixture, fixture, fixture, fixture, fixture, null!, fixture)),
            ("intrinsics", () => new(fixture, fixture, fixture, fixture, fixture, fixture, fixture, fixture, null!)),
        };
        foreach (var (parameter, create) in constructors)
        {
            Assert.Equal(parameter, Assert.Throws<ArgumentNullException>(create).ParamName);
        }
        Assert.Throws<ArgumentNullException>(() => fixture.Analyzer.Analyze(null!));
        Assert.Throws<ArgumentNullException>(() =>
            fixture.Analyzer.Analyze(request with { Method = null! }));
        Assert.Throws<ArgumentNullException>(() =>
            fixture.Analyzer.Analyze(request with { RootMap = null! }));
        Assert.Throws<ArgumentNullException>(() =>
            fixture.Analyzer.Analyze(request with { ByRefEffects = null! }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DeclinesArgumentPromotionWithoutAnOwnedStorageEffect(int missingFact)
    {
        var fixture = new Fixture();
        var method = fixture.LoopMethod();
        var request = fixture.Request(method, fixture.EmptyRootMap());
        request = missingFact switch
        {
            0 => request with { Method = method with { Header = method.Header with { MethodInstance = null } } },
            1 => request with { ByRefEffects = new(ImmutableDictionary<string, ManagedByRefMethodEffects>.Empty) },
            _ => request with
            {
                ByRefEffects = new(request.ByRefEffects.Methods.SetItem(
                    fixture.Body.CanonicalName, ManagedByRefMethodEffects.Empty)),
            },
        };

        var analysis = fixture.Analyzer.Analyze(request);

        Assert.Same(ManagedAggregateValueAnalysis.Empty, analysis);
    }

    private static StructuredMethod RewriteInstructions(
        StructuredMethod method, Func<CilInstruction, CilInstruction> rewrite) => method with
        {
            Header = method.Header with { Instructions = [.. method.Header.Instructions.Select(rewrite)] },
            Blocks = method.Blocks.ToImmutableDictionary(pair => pair.Key,
            pair => pair.Value with { Instructions = [.. pair.Value.Instructions.Select(rewrite)] }),
        };

    private sealed class Fixture :
        IFieldRepository,
        IMethodRepository,
        ITypeDefinitionResolver,
        IValueLayoutProvider,
        IInstanceFieldLayoutProvider,
        IArgumentSignatureTypeResolver,
        ICilTypeOperandResolver,
        ITargetLayout,
        IRuntimeIntrinsicRegistry
    {
        private static readonly AssemblyIdentity Assembly = new("aggregate-value-tests");
        private static readonly EntityKey ContainerKey = new(Assembly, 0x02000001);
        private static readonly EntityKey AggregateKey = new(Assembly, 0x02000002);
        private static readonly EntityKey ViewKey = new(Assembly, 0x02000003);
        private static readonly EntityKey LargeViewKey = new(Assembly, 0x02000004);
        private static readonly EntityKey ReferenceViewKey = new(Assembly, 0x02000005);
        private readonly FieldDefinitionModel _low;
        private readonly FieldDefinitionModel _high;
        private readonly FieldDefinitionModel _viewHigh;
        private readonly MethodDefinitionModel _target;
        private readonly MethodDefinitionModel _unsafeAs;
        private readonly MethodDefinitionModel _unsafeAdd;
        private readonly MethodDefinitionModel _virtualTarget;
        private readonly MethodDefinitionModel _constructor;
        private readonly MethodDefinitionModel _multiTarget;
        private readonly MethodDefinitionModel _unsafeAsTwo;
        private readonly MethodDefinitionModel _unsafeAsLarge;
        private readonly MethodDefinitionModel _unsafeAsReference;
        private readonly MethodDefinitionModel _aliasTarget;

        private readonly WasmTargetLayout _layout;

        public Fixture(WasmTarget target = WasmTarget.Wasm32)
        {
            _layout = target == WasmTarget.Wasm32 ? WasmTargetLayout.Wasm32 : WasmTargetLayout.Wasm64;
            Aggregate = CliTypeIdentity.Named(Assembly, "Tests", "Aggregate", true);
            View = CliTypeIdentity.Named(Assembly, "Tests", "View", true);
            LargeView = CliTypeIdentity.Named(Assembly, "Tests", "LargeView", true);
            ReferenceView = CliTypeIdentity.Named(
                Assembly,
                "Tests",
                "ReferenceView",
                true);
            ByRefAggregate = CliTypeIdentity.ManagedByReference(Aggregate);
            _low = Field(1, AggregateKey, "Low");
            _high = Field(2, AggregateKey, "High");
            _viewHigh = Field(3, ViewKey, "High");
            Low = new(_low, Aggregate, _low.SignatureType);
            High = new(_high, Aggregate, _high.SignatureType);
            ViewHigh = new(_viewHigh, View, _viewHigh.SignatureType);
            var bodyDefinition = Method(
                1,
                "Body",
                MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    Aggregate,
                    ByRefAggregate));
            Body = new(bodyDefinition, Container, [], bodyDefinition.Signature);
            _target = Method(
                2,
                "Target",
                MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    ByRefAggregate));
            Target = new(_target, Container, [], _target.Signature);
            _unsafeAs = Method(
                3,
                "UnsafeAs",
                MethodSignatureModel.Create(
                    CliTypeIdentity.ManagedByReference(View),
                    ByRefAggregate));
            UnsafeAs = new(_unsafeAs, Container, [], _unsafeAs.Signature);
            _unsafeAdd = Method(
                4,
                "UnsafeAdd",
                MethodSignatureModel.Create(
                    ByRefAggregate,
                    ByRefAggregate,
                    CliTypeIdentity.FromStackKind(CliValueKind.I4)));
            UnsafeAdd = new(_unsafeAdd, Container, [], _unsafeAdd.Signature);
            _virtualTarget = Method(
                5,
                "VirtualTarget",
                MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.Void)),
                isStatic: false);
            VirtualTarget = new(
                _virtualTarget,
                Aggregate,
                [],
                _virtualTarget.Signature);
            _constructor = Method(
                6,
                ".ctor",
                MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    ByRefAggregate),
                isStatic: false);
            Constructor = new(_constructor, Container, [], _constructor.Signature);
            _multiTarget = Method(
                7,
                "MultiTarget",
                MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    ByRefAggregate,
                    ByRefAggregate));
            MultiTarget = new(_multiTarget, Container, [], _multiTarget.Signature);
            _unsafeAsTwo = Method(
                8,
                "UnsafeAsTwo",
                MethodSignatureModel.Create(
                    CliTypeIdentity.ManagedByReference(View),
                    ByRefAggregate,
                    CliTypeIdentity.FromStackKind(CliValueKind.I4)));
            UnsafeAsTwo = new(_unsafeAsTwo, Container, [], _unsafeAsTwo.Signature);
            _unsafeAsLarge = Method(
                9,
                "UnsafeAsLarge",
                MethodSignatureModel.Create(
                    CliTypeIdentity.ManagedByReference(LargeView),
                    ByRefAggregate));
            UnsafeAsLarge = new(
                _unsafeAsLarge,
                Container,
                [],
                _unsafeAsLarge.Signature);
            _unsafeAsReference = Method(
                10,
                "UnsafeAsReference",
                MethodSignatureModel.Create(
                    CliTypeIdentity.ManagedByReference(ReferenceView),
                    ByRefAggregate));
            UnsafeAsReference = new(
                _unsafeAsReference,
                Container,
                [],
                _unsafeAsReference.Signature);
            _aliasTarget = Method(
                11,
                "AliasTarget",
                MethodSignatureModel.Create(
                    ByRefAggregate,
                    ByRefAggregate));
            AliasTarget = new(
                _aliasTarget,
                Container,
                [],
                _aliasTarget.Signature);
            Analyzer = new(this, this, this, this, this, this, this, this, this);
        }

        public ManagedAggregateValueAnalyzer Analyzer { get; }

        public MethodInstanceModel Body { get; }

        public MethodInstanceModel Target { get; }

        public MethodInstanceModel UnsafeAs { get; }

        public MethodInstanceModel UnsafeAdd { get; }

        public MethodInstanceModel VirtualTarget { get; }

        public MethodInstanceModel Constructor { get; }

        public MethodInstanceModel MultiTarget { get; }

        public MethodInstanceModel UnsafeAsTwo { get; }

        public MethodInstanceModel UnsafeAsLarge { get; }

        public MethodInstanceModel UnsafeAsReference { get; }

        public MethodInstanceModel AliasTarget { get; }

        public CliTypeIdentity Aggregate { get; }

        public CliTypeIdentity View { get; }

        public CliTypeIdentity LargeView { get; }

        public CliTypeIdentity ReferenceView { get; }

        public CliTypeIdentity ByRefAggregate { get; }

        public FieldInstanceModel Low { get; }

        public FieldInstanceModel High { get; }

        public FieldInstanceModel ViewHigh { get; }

        WasmTargetLayout ITargetLayout.Target => _layout;

        public ValueLayout? AggregateLayout { get; set; }

        public int LowFieldOffset { get; set; }

        public bool References { get; init; } = true;

        public CliTypeIdentity LowFieldType { get; set; } = CliTypeIdentity.FromStackKind(CliValueKind.I4);

        public ManagedAggregateValueAnalysis Analyze(
            StructuredMethod method,
            MethodRootMap? rootMap = null,
            ManagedByRefMethodEffects? targetEffects = null,
            bool enabled = true) => Analyzer.Analyze(Request(
            method,
            rootMap ?? EmptyRootMap(),
            targetEffects,
            enabled));

        public ManagedAggregateValueAnalysisRequest Request(
            StructuredMethod method,
            MethodRootMap rootMap,
            ManagedByRefMethodEffects? targetEffects = null,
            bool enabled = true) => new(
            method,
            rootMap,
            new(ImmutableDictionary<string, ManagedByRefMethodEffects>.Empty
                .Add(Body.CanonicalName, new([0], [], []))
                .Add(Target.CanonicalName, targetEffects ?? ManagedByRefMethodEffects.Empty)
                .Add(AliasTarget.CanonicalName,
                    targetEffects ?? ManagedByRefMethodEffects.Empty)
                .Add(MultiTarget.CanonicalName, ManagedByRefMethodEffects.Empty)),
            enabled)
            {
                References = References,
            };

        public MethodRootMap EmptyRootMap() => new(Body.Definition.Key, [], []);

        public StructuredMethod TwoOwnedStoragesMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.StoreField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress, CliValueKind.I4),
                Step(I(3, CilOperation.LoadLocalAddress, new CilOperand.Index(1))),
                Step(I(4, CilOperation.Call, new CilOperand.MethodInstance(Target)), CliValueKind.ManagedAddress),
                Step(I(5, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(6, CilOperation.Call, new CilOperand.MethodInstance(Target)), CliValueKind.ManagedAddress),
                Step(I(7, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(8, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(9, CilOperation.Pop), CliValueKind.I4),
                Step(I(10, CilOperation.LoadLocalAddress, new CilOperand.Index(1))),
                Step(I(11, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(12, CilOperation.Pop), CliValueKind.I4),
            };
            var id = new StructuredBlockId(0);
            return Graph(id, ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(id, Block(id, steps, new StructuredFallthroughExit(null))), steps,
                [CliValueKind.ValueType, CliValueKind.ValueType], [Aggregate, Aggregate]);
        }

        public StructuredMethod LoopMethod()
        {
            var first = new StructuredBlockId(0);
            var loop = new StructuredBlockId(1);
            var exit = new StructuredBlockId(2);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Branch), Array.Empty<CliValueKind>()),
                Step(I(3, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(4, CilOperation.LoadField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(5, CilOperation.Pop), CliValueKind.I4),
                Step(I(6, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    CliValueKind.ManagedAddress),
                Step(I(8, CilOperation.StoreField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress, CliValueKind.I4),
                Step(I(9, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(10, CilOperation.Call, new CilOperand.MethodInstance(Target)),
                    CliValueKind.ManagedAddress),
                Step(I(11, CilOperation.LoadInt32, new CilOperand.ConstantI4(0))),
                Step(I(12, CilOperation.BranchIfTrue), CliValueKind.I4),
                Step(I(13, CilOperation.Nop)),
            };
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>
                .Empty
                .Add(first, Block(first, steps[0..2], new StructuredBranchExit(2, loop)))
                .Add(loop, Block(
                    loop,
                    steps[3..12],
                    new StructuredConditionalExit(
                        new(12, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null),
                        loop,
                        exit)))
                .Add(exit, Block(exit, steps[13..14], new StructuredFallthroughExit(null)));
            return Graph(first, blocks, steps, [CliValueKind.ManagedAddress], [ByRefAggregate]);
        }

        public StructuredMethod UnsafeAsMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.Call, new CilOperand.MethodInstance(UnsafeAs)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadField, new CilOperand.FieldInstance(ViewHigh)),
                    CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.Pop), CliValueKind.I4),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod SameTargetConditionalMethod()
        {
            var entry = new StructuredBlockId(0);
            var join = new StructuredBlockId(1);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.BranchIfTrue)),
                Step(I(3, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(4, CilOperation.LoadField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(5, CilOperation.Pop), CliValueKind.I4),
                Step(I(6, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    CliValueKind.ManagedAddress),
                Step(I(8, CilOperation.StoreField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress, CliValueKind.I4),
            };
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>
                .Empty
                .Add(entry, Block(
                    entry,
                    steps[0..2],
                    new StructuredConditionalExit(
                        new(2, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null),
                        join,
                        join)))
                .Add(join, Block(join, steps[3..9], new StructuredFallthroughExit(null)));
            return Graph(entry, blocks, steps, [CliValueKind.ManagedAddress], [ByRefAggregate]);
        }

        public StructuredMethod ObjectAccessMethod()
        {
            var i4 = new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.I4));
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadObject, i4), CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.Pop), CliValueKind.I4),
                Step(I(4, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(5, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(High)),
                    CliValueKind.ManagedAddress),
                Step(I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    CliValueKind.ManagedAddress),
                Step(I(7, CilOperation.StoreObject, i4),
                    CliValueKind.ManagedAddress, CliValueKind.I4),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod WholeValueLoadMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.I4),
                Step(I(3, CilOperation.LoadArgument, new CilOperand.Index(0))),
                Step(I(4, CilOperation.Pop), CliValueKind.ValueType),
            };
            return SingleBlock(steps);
        }

        // The whole value is loaded and stored back, then a loop reads a field;
        // the store sits before the loop or inside it.
        public StructuredMethod StoreThenLoopMethod(bool storeInLoop)
        {
            var store = new StructuredBlockId(0);
            var loop = new StructuredBlockId(1);
            var storeSteps = new[]
            {
                Step(I(0, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(1, CilOperation.StoreLocal, new CilOperand.Index(0)), CliValueKind.ValueType),
            };
            var loopSteps = new[]
            {
                Step(I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(3, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(4, CilOperation.Pop), CliValueKind.I4),
            };
            StructuredRegion Code(StructuredBlockId id) => new StructuredCode(new(id, StructuredBlockRole.Owner));
            StructuredRegion Loop(params StructuredRegion[] body) =>
                new StructuredLoop(null, false, new([.. body]), StructuredSequence.Empty, StructuredSequence.Empty);
            return Graph(store,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                    .Add(store, Block(store, storeSteps, new StructuredFallthroughExit(loop)))
                    .Add(loop, Block(loop, loopSteps, new StructuredFallthroughExit(null))),
                [.. storeSteps, .. loopSteps], [CliValueKind.ValueType], [Aggregate],
                new(storeInLoop ? [Loop(Code(store), Code(loop))] : [Code(store), Loop(Code(loop))]));
        }

        public StructuredMethod LocalValueMethod(bool storeWholeValue)
        {
            var id = new StructuredBlockId(0);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.I4),
                Step(I(3, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(4, storeWholeValue ? CilOperation.StoreLocal : CilOperation.Pop,
                    storeWholeValue ? new CilOperand.Index(0) : new CilOperand.None()), CliValueKind.ValueType),
            };
            return Graph(id,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(
                    id, Block(id, steps, new StructuredFallthroughExit(null))),
                steps, [CliValueKind.ValueType], [Aggregate]);
        }

        public StructuredMethod FieldRoundTripMethod()
        {
            var id = new StructuredBlockId(0);
            var field = new FieldInstanceModel(_low, Aggregate, LowFieldType);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadNull), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.StoreField, new CilOperand.FieldInstance(field)),
                    CliValueKind.ManagedAddress, LowFieldType.StackKind),
                Step(I(3, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(4, CilOperation.LoadField, new CilOperand.FieldInstance(field)), CliValueKind.ManagedAddress),
                Step(I(5, CilOperation.Pop), LowFieldType.StackKind),
            };
            return Graph(id,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(
                    id, Block(id, steps, new StructuredFallthroughExit(null))),
                steps, [CliValueKind.ValueType], [Aggregate]);
        }

        public StructuredMethod ClearMethod(CliTypeIdentity type)
        {
            var id = new StructuredBlockId(0);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.InitializeObject, new CilOperand.TypeIdentity(type)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(3, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(4, CilOperation.Pop), CliValueKind.I4),
            };
            return Graph(id,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(
                    id, Block(id, steps, new StructuredFallthroughExit(null))),
                steps, [CliValueKind.ValueType], [Aggregate]);
        }

        public StructuredMethod FieldClearMethod(FieldInstanceModel field)
        {
            var id = new StructuredBlockId(0);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.InitializeObject, new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4))),
                    CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(4, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(5, CilOperation.Pop), CliValueKind.I4),
            };
            return Graph(id,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(
                    id, Block(id, steps, new StructuredFallthroughExit(null))),
                steps, [CliValueKind.ValueType], [Aggregate]);
        }

        public StructuredMethod UnknownClearMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgument, new CilOperand.Index(1))),
                Step(I(1, CilOperation.InitializeObject, new CilOperand.TypeIdentity(Aggregate)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), CliValueKind.ManagedAddress),
                Step(I(4, CilOperation.StoreField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress, CliValueKind.I4),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod NonAggregateLocalAddressMethod()
        {
            var id = new StructuredBlockId(0);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.Pop), CliValueKind.ManagedAddress),
            };
            return Graph(id,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(
                    id, Block(id, steps, new StructuredFallthroughExit(null))),
                steps, [CliValueKind.I4], [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        }

        public StructuredMethod MixedLocalAddressMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.StoreLocal, new CilOperand.Index(0)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(1))),
                Step(I(3, CilOperation.Pop), CliValueKind.ManagedAddress),
                Step(I(4, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(5, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(6, CilOperation.Pop), CliValueKind.I4),
            };
            var method = SingleBlock(steps);
            return method with
            {
                Header = method.Header with
                {
                    Locals = [CliValueKind.ManagedAddress, CliValueKind.I4],
                    LocalSignatureTypes = [ByRefAggregate,
                        CliTypeIdentity.FromStackKind(CliValueKind.I4)],
                },
            };
        }

        public StructuredMethod NonScalarObjectAccessMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadObject, new CilOperand.TypeIdentity(Aggregate)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.ValueType),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod FallthroughAddressMethod()
        {
            var entry = new StructuredBlockId(0);
            var successor = new StructuredBlockId(1);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.I4),
            };
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(entry, Block(entry, steps[0..1], new StructuredFallthroughExit(successor)))
                .Add(successor, new(successor, 1, steps[1..3].Select(step => step.Instruction).ToImmutableArray(),
                    [CliValueKind.ManagedAddress], new StructuredFallthroughExit(null))
                { EndOffset = 3 });
            return Graph(entry, blocks, steps, [], []);
        }

        public StructuredMethod ExternalFieldAddressMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.StoreLocal, new CilOperand.Index(0)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadArgument, new CilOperand.Index(1))),
                Step(I(3, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(4, CilOperation.Pop), CliValueKind.ManagedAddress),
                Step(I(5, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(6, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(7, CilOperation.Pop), CliValueKind.I4),
            };
            var method = SingleBlock(steps);
            return method with
            {
                Header = method.Header with
                {
                    Locals = [CliValueKind.ManagedAddress],
                    LocalSignatureTypes = [ByRefAggregate],
                },
            };
        }

        public StructuredMethod UnknownAddressProducerMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.StoreLocal, new CilOperand.Index(0)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadStaticField, new CilOperand.FieldInstance(Low))),
                Step(I(3, CilOperation.Pop), CliValueKind.ManagedAddress),
                Step(I(4, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(5, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(6, CilOperation.Pop), CliValueKind.I4),
            };
            var method = SingleBlock(steps);
            return method with
            {
                Header = method.Header with
                {
                    Locals = [CliValueKind.ManagedAddress],
                    LocalSignatureTypes = [ByRefAggregate],
                },
            };
        }

        public StructuredMethod UnresolvedFieldAddressMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(ViewHigh)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.ManagedAddress),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod MismatchedObjectAccessMethod()
        {
            var integer64 = CliTypeIdentity.FromStackKind(CliValueKind.I8);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadObject, new CilOperand.TypeIdentity(integer64)), CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.Pop), CliValueKind.I8),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod StructuredExitMethod(bool terminal)
        {
            var entry = new StructuredBlockId(0);
            var exit = new StructuredBlockId(1);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.I4),
                Step(I(3, terminal ? CilOperation.Return : CilOperation.Leave,
                    terminal ? new CilOperand.None() : new CilOperand.BranchTarget(4))),
                Step(I(4, CilOperation.Nop)),
            };
            var entryExit = terminal
                ? (StructuredBlockExit)new StructuredTerminalExit(steps[3].Instruction)
                : new StructuredLeaveExit(3, exit);
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(entry, Block(entry, steps[0..3], entryExit))
                .Add(exit, Block(exit, steps[4..5], new StructuredFallthroughExit(null)));
            return Graph(entry, blocks, steps, [], []);
        }

        public StructuredMethod InvalidExitStackMethod()
        {
            var entry = new StructuredBlockId(0);
            var storage = new StructuredBlockId(1);
            var successor = new StructuredBlockId(2);
            var steps = new[]
            {
                Step(I(0, CilOperation.Branch)),
                Step(I(1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(2, CilOperation.Pop), CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.Nop), CliValueKind.ManagedAddress),
            };
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(entry, new(entry, 0, [], [], new StructuredFallthroughExit(successor)) { EndOffset = 0 })
                .Add(storage, Block(storage, steps[1..3], new StructuredFallthroughExit(null)))
                .Add(successor, new(successor, 3, [steps[3].Instruction],
                    [CliValueKind.ManagedAddress], new StructuredFallthroughExit(null))
                { EndOffset = 4 });
            return Graph(entry, blocks, steps, [], []);
        }

        public StructuredMethod WholeValueStoreMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.I4),
                Step(I(3, CilOperation.LoadArgument, new CilOperand.Index(0))),
                Step(I(4, CilOperation.StoreArgument, new CilOperand.Index(0)),
                    CliValueKind.ValueType),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod OffsetAliasMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Call, new CilOperand.MethodInstance(UnsafeAdd)),
                    CliValueKind.ManagedAddress, CliValueKind.I4),
                Step(I(3, CilOperation.LoadField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(4, CilOperation.Pop), CliValueKind.I4),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod ReturnedAliasMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.Call, new CilOperand.MethodInstance(AliasTarget)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.Pop), CliValueKind.I4),
                Step(I(4, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    CliValueKind.ManagedAddress),
                Step(I(6, CilOperation.StoreField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress, CliValueKind.I4),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod CallVirtualMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.CallVirtual,
                        new CilOperand.MethodInstance(VirtualTarget)),
                    CliValueKind.ManagedAddress),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod NewObjectMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.NewObject,
                        new CilOperand.MethodInstance(Constructor)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.ManagedReference),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod DuplicateReadOnlyCallMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Call, new CilOperand.MethodInstance(MultiTarget)),
                    CliValueKind.ManagedAddress,
                    CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(4, CilOperation.LoadField, new CilOperand.FieldInstance(Low)), CliValueKind.ManagedAddress),
                Step(I(5, CilOperation.Pop), CliValueKind.I4),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod DuplicateAddressMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.Duplicate), CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.ManagedAddress, CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.Pop), CliValueKind.ManagedAddress),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod AddressConsumerMethod(CilOperation operation)
        {
            CilOperand operand = operation == CilOperation.CallIndirect
                ? new CilOperand.CallSite(MethodSignatureModel.Create(
                    CliValueKind.Void, [CliValueKind.ManagedAddress]))
                : new CilOperand.None();
            var steps = new List<InstructionStep>
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, operation, operand), CliValueKind.ManagedAddress),
            };
            if (operation == CilOperation.CompareExchange)
            {
                steps.Add(Step(I(2, CilOperation.Pop), CliValueKind.I4));
            }
            return SingleBlock([.. steps]);
        }

        public StructuredMethod IndirectAddressCallMethod()
        {
            var callSite = MethodSignatureModel.Create(CliValueKind.Void,
                [CliValueKind.ManagedAddress]);
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.CallIndirect, new CilOperand.CallSite(callSite)),
                    CliValueKind.ManagedAddress),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod UnsafeAliasViewMethod(CliValueKind viewKind)
        {
            var viewType = CliTypeIdentity.FromStackKind(viewKind);
            var target = UnsafeAs with
            {
                Signature = MethodSignatureModel.Create(
                    CliTypeIdentity.ManagedByReference(viewType),
                    ByRefAggregate),
            };
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.Call, new CilOperand.MethodInstance(target)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.LoadObject, new CilOperand.TypeIdentity(viewType)),
                    CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.Pop), viewKind),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod UnsafeBadArityMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Call, new CilOperand.MethodInstance(UnsafeAsTwo)),
                    CliValueKind.ManagedAddress,
                    CliValueKind.I4),
                Step(I(3, CilOperation.Pop), CliValueKind.ManagedAddress),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod UnsafeOversizedViewMethod() =>
            UnsafeViewMethod(UnsafeAsLarge);

        public StructuredMethod UnsafeReferenceViewMethod() =>
            UnsafeViewMethod(UnsafeAsReference);

        public StructuredMethod UnresolvedFieldWriteMethod()
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.StoreField, new CilOperand.FieldInstance(ViewHigh)),
                    CliValueKind.ManagedAddress,
                    CliValueKind.I4),
            };
            return SingleBlock(steps);
        }

        private StructuredMethod UnsafeViewMethod(MethodInstanceModel target)
        {
            var steps = new[]
            {
                Step(I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(1, CilOperation.Call, new CilOperand.MethodInstance(target)),
                    CliValueKind.ManagedAddress),
                Step(I(2, CilOperation.Pop), CliValueKind.ManagedAddress),
            };
            return SingleBlock(steps);
        }

        public StructuredMethod AmbiguousMergeMethod(bool swapBranches = false)
        {
            var entry = new StructuredBlockId(0);
            var owned = new StructuredBlockId(1);
            var external = new StructuredBlockId(2);
            var join = new StructuredBlockId(3);
            var steps = new[]
            {
                Step(I(0, CilOperation.BranchIfTrue)),
                Step(I(1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
                Step(I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
                    CliValueKind.ManagedAddress),
                Step(I(3, CilOperation.Branch)),
                Step(I(4, CilOperation.LoadArgument, new CilOperand.Index(1))),
                Step(I(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
                    CliValueKind.ManagedAddress),
                Step(I(6, CilOperation.Branch)),
                Step(I(7, CilOperation.LoadLocal, new CilOperand.Index(0))),
                Step(I(8, CilOperation.LoadField, new CilOperand.FieldInstance(Low)),
                    CliValueKind.ManagedAddress),
                Step(I(9, CilOperation.Pop), CliValueKind.I4),
            };
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>
                .Empty
                .Add(entry, Block(
                    entry,
                    [],
                    new StructuredConditionalExit(
                        new(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null),
                        swapBranches ? external : owned,
                        swapBranches ? owned : external)))
                .Add(owned, Block(owned, steps[1..3], new StructuredBranchExit(3, join)))
                .Add(external, Block(
                    external,
                    steps[4..6],
                    new StructuredBranchExit(6, join)))
                .Add(join, Block(join, steps[7..10], new StructuredFallthroughExit(null)));
            return Graph(entry, blocks, steps, [CliValueKind.ManagedAddress], [ByRefAggregate]);
        }

        public FieldDefinitionModel GetField(EntityKey key) => key == _low.Key
            ? _low
            : key == _high.Key
                ? _high
                : _viewHigh;

        public MethodDefinitionModel GetMethod(EntityKey key) => key == _target.Key
            ? _target
            : key == _unsafeAs.Key
                ? _unsafeAs
                : key == _unsafeAdd.Key
                    ? _unsafeAdd
                : key == _virtualTarget.Key
                    ? _virtualTarget
                : key == _constructor.Key
                    ? _constructor
                : key == _multiTarget.Key
                    ? _multiTarget
                : key == _unsafeAsTwo.Key
                    ? _unsafeAsTwo
                : key == _unsafeAsLarge.Key
                    ? _unsafeAsLarge
                : key == _unsafeAsReference.Key
                    ? _unsafeAsReference
                : key == _aliasTarget.Key
                    ? _aliasTarget
                    : Body.Definition;

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            identity.Equals(View)
                ? new(ViewKey, "Tests", "View", true, [_viewHigh.Key], [])
                : new(AggregateKey, "Tests", "Aggregate", true, [_low.Key, _high.Key], []);

        public ValueLayout GetValueLayout(CliTypeIdentity type) => type.Equals(Aggregate) && AggregateLayout is { } configured
            ? configured
            : type.Equals(LargeView)
            ? new(type, 12, 4, [])
            : type.Equals(ReferenceView)
                ? new(type, 8, 4, [0])
                : new(type, 8, 4, []);

        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            GetFieldLayout(field.Definition.Key);

        public FieldLayout GetFieldLayout(EntityKey field) => field == _low.Key
            ? new(LowFieldOffset)
            {
                Size = LowFieldType.StackKind switch
                {
                    CliValueKind.I4 => 4,
                    CliValueKind.I8 => 8,
                    _ => _layout.ObjectReferenceSize,
                },
                Type = LowFieldType,
            }
            : new(4)
            {
                Size = 4,
                Type = CliTypeIdentity.FromStackKind(CliValueKind.I4),
            };

        CliTypeIdentity IArgumentSignatureTypeResolver.Resolve(
            StructuredMethodHeader header,
            int index) => (header.MethodInstance?.Signature ?? header.Method.Signature)
            .ParameterSignatureTypes[index];

        CliTypeIdentity ICilTypeOperandResolver.Resolve(
            CilInstruction instruction,
            MethodInstanceModel? methodInstance) =>
            ((CilOperand.TypeIdentity)instruction.Operand).Value;

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            if (method == _unsafeAs.Key)
            {
                intrinsic = RuntimeIntrinsic.UnsafeAs;
                return true;
            }
            if (method == _unsafeAdd.Key)
            {
                intrinsic = RuntimeIntrinsic.UnsafeAdd;
                return true;
            }
            if (method == _unsafeAsTwo.Key ||
                method == _unsafeAsLarge.Key ||
                method == _unsafeAsReference.Key)
            {
                intrinsic = RuntimeIntrinsic.UnsafeAs;
                return true;
            }
            intrinsic = default;
            return false;
        }

        private StructuredMethod SingleBlock(InstructionStep[] steps)
        {
            var id = new StructuredBlockId(0);
            return Graph(
                id,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(
                    id,
                    Block(id, steps, new StructuredFallthroughExit(null))),
                steps,
                [],
                []);
        }

        private StructuredMethod Graph(
            StructuredBlockId entry,
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> blocks,
            IEnumerable<InstructionStep> steps,
            ImmutableArray<CliValueKind> locals,
            ImmutableArray<CliTypeIdentity> localTypes,
            StructuredSequence? body = null)
        {
            var all = steps.ToImmutableArray();
            return new(
                new(
                    Body.Definition,
                    Body,
                    4,
                    locals,
                    localTypes,
                    [.. all.Select(step => step.Instruction)]),
                entry,
                blocks,
                body ?? StructuredSequence.Empty,
                [],
                [],
                all.ToImmutableDictionary(
                    step => step.Instruction.Offset,
                    step => step.EntryStack));
        }

        private static StructuredBlockDefinition Block(
            StructuredBlockId id,
            IEnumerable<InstructionStep> steps,
            StructuredBlockExit exit)
        {
            var instructions = steps.Select(step => step.Instruction).ToImmutableArray();
            return new(id, instructions.IsEmpty ? id.Value : instructions[0].Offset,
                instructions, [], exit)
            {
                EndOffset = instructions.IsEmpty
                    ? id.Value
                    : instructions[^1].NextOffset,
            };
        }

        private static InstructionStep Step(
            CilInstruction instruction,
            params CliValueKind[] entryStack) => new(instruction, [.. entryStack]);

        private static CilInstruction I(
            int offset,
            CilOperation operation,
            CilOperand? operand = null) => new(
                offset,
                offset + 1,
                operation,
                operand ?? new CilOperand.None());

        private static FieldDefinitionModel Field(
            int token,
            EntityKey owner,
            string name) => new(
                new(Assembly, 0x04000000 + token),
                owner,
                name,
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                false);

        private static MethodDefinitionModel Method(
            int token,
            string name,
            MethodSignatureModel signature,
            bool isStatic = true) => new(
                new(Assembly, 0x06000000 + token),
                ContainerKey,
                name,
                isStatic,
                signature,
                1);

        private static CliTypeIdentity Container { get; } =
            CliTypeIdentity.Named(Assembly, "Tests", "Container", false);

        private readonly record struct InstructionStep(
            CilInstruction Instruction,
            ImmutableArray<CliValueKind> EntryStack);
    }
}
