using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.GarbageCollection.Tests;

public sealed class StackStorageAnchorPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("StorageAnchors");
    private static readonly EntityKey PairKey = new(Assembly, 0x02000001);
    private static readonly EntityKey OtherKey = new(Assembly, 0x02000002);
    private static readonly EntityKey HostKey = new(Assembly, 0x02000003);
    private static readonly EntityKey EntryKey = new(Assembly, 0x06000001);
    private static readonly EntityKey ProducerKey = new(Assembly, 0x06000002);
    private static readonly EntityKey OtherProducerKey = new(Assembly, 0x06000003);
    private static readonly EntityKey CollectKey = new(Assembly, 0x06000004);
    private static readonly EntityKey ConsumeKey = new(Assembly, 0x06000005);
    private static readonly EntityKey PairField = new(Assembly, 0x04000001);
    private static readonly EntityKey OtherField = new(Assembly, 0x04000002);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Tests", "Pair", true);
    private static readonly CliTypeIdentity Other = CliTypeIdentity.Named(Assembly, "Tests", "Other", true);
    private static readonly CliTypeIdentity I4 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);

    [Fact]
    public void CapturesTheOriginalProducerAndCopiesItThroughProjectionAndLocalReload()
    {
        var method = Body(
            [Call(0, ProducerKey), Field(1, PairField), Index(2, CilOperation.StoreLocal, 0),
             Call(3, CollectKey), Index(4, CilOperation.LoadLocal, 0), Call(5, ConsumeKey), I(6, CilOperation.Return)],
            [CliTypeIdentity.ManagedByReference(I4)]);
        var plan = Plan(method, (3, Local(0, Pair)), (5, Stack(0, Pair)));
        Assert.Equal(new[] { Stack(0, Pair), Local(0, Pair) }, plan.Anchors.ToArray());
        Assert.Equal(new StackStorageAnchorAssignment.Capture(0, 0), Assert.Single(plan.Assignments[0]));
        Assert.Equal(new StackStorageAnchorAssignment.Copy(0, 0), Assert.Single(plan.Assignments[1]));
        Assert.Equal(new StackStorageAnchorAssignment.Copy(1, 0), Assert.Single(plan.Assignments[2]));
        Assert.Equal(new StackStorageAnchorAssignment.Copy(0, 1), Assert.Single(plan.Assignments[4]));
        Assert.False(plan.Assignments.ContainsKey(3));
        Assert.False(plan.Assignments.ContainsKey(5));
    }

    [Fact]
    public void ACollectingConsumerCanDemandTheCompleteBackingOfItsProjectedScalarByref()
    {
        var method = Body([Call(0, ProducerKey), Field(1, PairField), Call(2, ConsumeKey), I(3, CilOperation.Return)]);
        var plan = Plan(method, (2, Stack(0, Pair)));
        Assert.Equal(Stack(0, Pair), Assert.Single(plan.Anchors));
        Assert.Equal(new StackStorageAnchorAssignment.Capture(0, 0), Assert.Single(plan.Assignments[0]));
        Assert.Equal(new StackStorageAnchorAssignment.Copy(0, 0), Assert.Single(plan.Assignments[1]));
    }

    [Fact]
    public void DuplicateCopiesDemandFromItsNewStackSlotBackToTheOriginalProducer()
    {
        var method = Body([Call(0, ProducerKey), I(1, CilOperation.Duplicate), Call(2, CollectKey),
            I(3, CilOperation.Pop), I(4, CilOperation.Pop), I(5, CilOperation.Return)]);
        var plan = Plan(method, (2, Stack(1, Pair)));
        Assert.Equal(new[] { Stack(0, Pair), Stack(1, Pair) }, plan.Anchors.ToArray());
        Assert.Equal(new StackStorageAnchorAssignment.Capture(0, 0), Assert.Single(plan.Assignments[0]));
        Assert.Equal(new StackStorageAnchorAssignment.Copy(1, 0), Assert.Single(plan.Assignments[1]));
    }

    [Fact]
    public void ScalarAndDifferentBackingOverwritesClearEveryAllocatedInactiveVariant()
    {
        var method = Body([Call(0, ProducerKey), Call(1, CollectKey), I(2, CilOperation.Pop), Constant(3),
            I(4, CilOperation.Pop), Call(5, OtherProducerKey), Call(6, CollectKey), I(7, CilOperation.Pop), I(8, CilOperation.Return)]);
        var plan = Plan(method, (1, Stack(0, Pair)), (6, Stack(0, Other)));
        var pair = Slot(plan, Stack(0, Pair));
        var other = Slot(plan, Stack(0, Other));
        Assert.Equal(2, plan.Anchors.Length);
        Assert.Contains(new StackStorageAnchorAssignment.Capture(pair, 0), plan.Assignments[0]);
        Assert.Contains(new StackStorageAnchorAssignment.Clear(other), plan.Assignments[0]);
        Assert.All(plan.Assignments[3], assignment => Assert.IsType<StackStorageAnchorAssignment.Clear>(assignment));
        Assert.Equal(2, plan.Assignments[3].Length);
        Assert.Contains(new StackStorageAnchorAssignment.Capture(other, 0), plan.Assignments[5]);
        Assert.Contains(new StackStorageAnchorAssignment.Clear(pair), plan.Assignments[5]);
    }

    [Fact]
    public void DifferentEnclosingLayoutsJoinThroughTheSameScalarProjectionWithoutLosingTheirBases()
    {
        var method = Body(
            [Constant(0), Branch(1, CilOperation.BranchIfTrue, 6),
             Call(2, ProducerKey), Field(3, PairField), Branch(4, CilOperation.Branch, 10), I(5, CilOperation.Return),
             Call(6, OtherProducerKey), Field(7, OtherField), Branch(8, CilOperation.Branch, 10), I(9, CilOperation.Return),
             Call(10, CollectKey), I(11, CilOperation.Pop), I(12, CilOperation.Return)]);
        var storage = AnalyzeStorage(method);
        var joined = Assert.Single(storage.Entries[10]);
        Assert.True(joined.CompleteBackingKnown);
        Assert.True(joined.CapturedBackings.SetEquals([Pair, Other]));
        var plan = Plan(method, storage, (10, Stack(0, Pair)), (10, Stack(0, Other)));
        var pair = Slot(plan, Stack(0, Pair));
        var other = Slot(plan, Stack(0, Other));
        Assert.Contains(new StackStorageAnchorAssignment.Clear(other), plan.Assignments[2]);
        Assert.Contains(new StackStorageAnchorAssignment.Clear(pair), plan.Assignments[6]);
        Assert.Contains(new StackStorageAnchorAssignment.Copy(pair, pair), plan.Assignments[3]);
        Assert.Contains(new StackStorageAnchorAssignment.Copy(other, other), plan.Assignments[7]);
        Assert.False(plan.Assignments.ContainsKey(5));
        Assert.False(plan.Assignments.ContainsKey(9));
    }

    [Fact]
    public void StableAndCapturedPathsCombineIntoOneCompleteBackingProof()
    {
        var method = Body(
            [Constant(0), Branch(1, CilOperation.BranchIfTrue, 6),
             Index(2, CilOperation.LoadLocalAddress, 0), Field(3, PairField), Branch(4, CilOperation.Branch, 9),
             I(5, CilOperation.Return), Call(6, ProducerKey), Field(7, PairField), Branch(8, CilOperation.Branch, 9),
             Call(9, CollectKey), I(10, CilOperation.Pop), I(11, CilOperation.Return)], [Pair]);
        var storage = AnalyzeStorage(method);
        var joined = Assert.Single(storage.Entries[9]);
        Assert.False(joined.BackingCovered);
        Assert.True(joined.CompleteBackingKnown);
        Assert.Equal(Pair, Assert.Single(joined.CapturedBackings));
        var plan = Plan(method, storage, (9, Stack(0, Pair)));
        Assert.Equal(new StackStorageAnchorAssignment.Clear(0), Assert.Single(plan.Assignments[2]));
        Assert.Equal(new StackStorageAnchorAssignment.Capture(0, 0), Assert.Single(plan.Assignments[6]));
    }

    [Fact]
    public void LoopReassignmentCopiesTheNewLocalBaseAndDoesNotKeepThePreviousProducer()
    {
        var reference = CliTypeIdentity.ManagedByReference(Pair);
        var method = Body([Call(0, ProducerKey), Index(1, CilOperation.StoreLocal, 0), Call(2, CollectKey),
            Index(3, CilOperation.LoadLocal, 1), Index(4, CilOperation.StoreLocal, 0), Constant(5),
            Branch(6, CilOperation.BranchIfTrue, 2), I(7, CilOperation.Return)], [reference, reference]);
        var plan = Plan(method, (2, Local(0, Pair)));
        var stack = Slot(plan, Stack(0, Pair));
        var first = Slot(plan, Local(0, Pair));
        var second = Slot(plan, Local(1, Pair));
        Assert.Equal(3, plan.Anchors.Length);
        Assert.Equal(new StackStorageAnchorAssignment.Copy(stack, second), Assert.Single(plan.Assignments[3]));
        Assert.Equal(new StackStorageAnchorAssignment.Copy(first, stack), Assert.Single(plan.Assignments[4]));
        Assert.Equal(new StackStorageAnchorAssignment.Clear(stack), Assert.Single(plan.Assignments[5]));
    }

    [Fact]
    public void ExceptionalDemandUsesTheOldLocalBeforeAThrowingProducerSucceeds()
    {
        var method = Body([Call(0, ProducerKey), Index(1, CilOperation.StoreLocal, 0),
            Call(2, ProducerKey), Index(3, CilOperation.StoreLocal, 0), Branch(4, CilOperation.Leave, 10),
            I(5, CilOperation.Pop), Call(6, CollectKey), Index(7, CilOperation.LoadLocal, 0),
            I(8, CilOperation.Pop), Branch(9, CilOperation.Leave, 10), I(10, CilOperation.Return)],
            [CliTypeIdentity.ManagedByReference(Pair)], [new(CilExceptionRegionKind.Catch, 2, 3, 5, 5, HostKey, null)]);
        var plan = Plan(method, (6, Local(0, Pair)));
        var stack = Slot(plan, Stack(0, Pair));
        var local = Slot(plan, Local(0, Pair));
        Assert.Equal(new StackStorageAnchorAssignment.Capture(stack, 0), Assert.Single(plan.Assignments[0]));
        Assert.Equal(new StackStorageAnchorAssignment.Copy(local, stack), Assert.Single(plan.Assignments[1]));
        Assert.Equal(new StackStorageAnchorAssignment.Clear(stack), Assert.Single(plan.Assignments[2]));
        Assert.Equal(new StackStorageAnchorAssignment.Clear(local), Assert.Single(plan.Assignments[3]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyDemandDoesNotInvokeContinuationAnalysis(bool emptyEntry)
    {
        var method = Body([I(0, CilOperation.Return)]);
        var continuations = new RecordingContinuations(Flow(method));
        var planner = CreatePlanner(continuations);
        var demands = ImmutableDictionary<int, ImmutableHashSet<StackStorageAnchor>>.Empty;
        if (emptyEntry) demands = demands.Add(0, []);
        Assert.Same(StackStorageAnchorPlan.Empty, planner.Plan(new(method, StackStorageAnalysis.Empty, demands)));
        Assert.Equal(0, continuations.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void InvalidDemandsFailBeforeCallingContinuationAnalysis(int invalid)
    {
        var method = Body([Call(0, ProducerKey), I(1, CilOperation.Pop), I(2, CilOperation.Return)], [I4]);
        var continuations = new RecordingContinuations(Flow(method));
        var offset = invalid == 0 ? 42 : 1;
        var anchor = invalid switch
        {
            1 => Stack(0, CliTypeIdentity.GenericParameter(false, 0)),
            2 => Stack(-1, Pair),
            3 => Stack(1, Pair),
            4 => Local(0, Pair),
            5 => Local(1, Pair),
            6 => new(new((StackStorageLocationKind)42, 0), Pair),
            _ => Stack(0, Pair),
        };
        var request = new StackStorageAnchorRequest(method, StackStorageAnalysis.Empty,
            ImmutableDictionary<int, ImmutableHashSet<StackStorageAnchor>>.Empty.Add(offset, [anchor]));
        Assert.Throws<ArgumentException>(() => CreatePlanner(continuations).Plan(request));
        Assert.Equal(0, continuations.Calls);
    }

    [Fact]
    public void ALocalCaptureCannotBeMisinterpretedAsAnEvaluationStackResult()
    {
        var method = Body([Call(0, ProducerKey), Index(1, CilOperation.StoreLocal, 0), Call(2, CollectKey),
            I(3, CilOperation.Return)], [CliTypeIdentity.ManagedByReference(Pair)]);
        var original = AnalyzeStorage(method);
        var malformed = original with
        {
            Transfers = original.Transfers.SetItem(1, new(new(StackStorageLocationKind.Local, 0), [], [Pair])),
        };
        var continuations = new RecordingContinuations(Flow(method));
        var request = new StackStorageAnchorRequest(method, malformed,
            ImmutableDictionary<int, ImmutableHashSet<StackStorageAnchor>>.Empty.Add(2, [Local(0, Pair)]));
        Assert.Throws<ArgumentException>(() => CreatePlanner(continuations).Plan(request));
        Assert.Equal(0, continuations.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MissingMismatchedOrIncompleteSeedEvidenceFailsBeforeContinuationAnalysis(int missing)
    {
        var method = Body([Call(0, ProducerKey), Call(1, CollectKey), I(2, CilOperation.Pop), I(3, CilOperation.Return)],
            [CliTypeIdentity.ManagedByReference(Pair)]);
        var original = AnalyzeStorage(method);
        var storage = missing switch
        {
            0 => StackStorageAnalysis.Empty,
            2 => original with { Entries = original.Entries.SetItem(1, [original.Entries[1][0] with { CompleteBackingKnown = false }]) },
            3 => original with { Locals = original.Locals.Clear() },
            4 => original with { Entries = original.Entries.SetItem(1, []) },
            _ => original,
        };
        var anchor = missing == 3 ? Local(0, Pair) : Stack(0, missing == 1 ? Other : Pair);
        var continuations = new RecordingContinuations(Flow(method));
        var request = new StackStorageAnchorRequest(method, storage,
            ImmutableDictionary<int, ImmutableHashSet<StackStorageAnchor>>.Empty.Add(1, [anchor]));
        Assert.Throws<ArgumentException>(() => CreatePlanner(continuations).Plan(request));
        Assert.Equal(0, continuations.Calls);
    }

    [Fact]
    public void MissingCapabilitiesAndRequestMembersFailAtTheirOwningBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => new StackStorageAnchorPlanner(null!));
        var method = Body([I(0, CilOperation.Return)]);
        var continuations = new RecordingContinuations(Flow(method));
        var planner = CreatePlanner(continuations);
        var request = new StackStorageAnchorRequest(method, StackStorageAnalysis.Empty, []);
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { Method = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { Storage = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { Demands = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { Demands = request.Demands.Add(0, null!) }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { Demands = request.Demands.Add(0, [Stack(0, null!)]) }));
        Assert.Equal(0, continuations.Calls);
    }

    private static StackStorageAnchor Stack(int index, CliTypeIdentity type) => new(new(StackStorageLocationKind.EvaluationStack, index), type);

    private static StackStorageAnchor Local(int index, CliTypeIdentity type) => new(new(StackStorageLocationKind.Local, index), type);

    private static int Slot(StackStorageAnchorPlan plan, StackStorageAnchor anchor)
    {
        var index = plan.Anchors.IndexOf(anchor);
        Assert.True(index >= 0);
        return index;
    }

    private static StackStorageAnchorPlan Plan(ManagedMethodBody method, params (int Offset, StackStorageAnchor Anchor)[] demands) =>
        Plan(method, AnalyzeStorage(method), demands);

    private static StackStorageAnchorPlan Plan(ManagedMethodBody method, StackStorageAnalysis storage,
        params (int Offset, StackStorageAnchor Anchor)[] demands)
    {
        var planner = CreatePlanner(new RecordingContinuations(Flow(method)));
        return planner.Plan(new(method, storage, demands.GroupBy(item => item.Offset)
            .ToImmutableDictionary(group => group.Key, group => group.Select(item => item.Anchor).ToImmutableHashSet())));
    }

    private static IStackStorageAnchorPlanner CreatePlanner(IStackStorageContinuationAnalyzer continuations) =>
        new IStackStorageAnchorPlanner[] { new StackStorageAnchorPlanner(continuations) }[0];

    private static StackStorageContinuations Flow(ManagedMethodBody method) =>
        ((IStackStorageContinuationAnalyzer)new StackStorageContinuationAnalyzer()).Analyze(method);

    private static StackStorageAnalysis AnalyzeStorage(ManagedMethodBody method)
    {
        var repository = new Repository();
        var analyzer = new IStackStorageTypeAnalyzer[]
        {
            new StackStorageTypeAnalyzer(repository, repository, repository, repository, new RecordingContinuations(Flow(method))),
        }[0];
        return analyzer.Analyze(method);
    }

    private static ManagedMethodBody Body(ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CliTypeIdentity> locals = default, ImmutableArray<CilExceptionRegion> regions = default)
    {
        var definition = Method(EntryKey, Void);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "Tests", "Host", false), [], definition.Signature);
        var localTypes = locals.IsDefault ? [] : locals;
        var body = new CilMethodBody(definition, 4, [.. localTypes.Select(type => type.StackKind)], instructions)
        {
            MethodInstance = method,
            LocalSignatureTypes = localTypes,
            ExceptionRegions = regions.IsDefault ? [] : regions,
        };
        var graph = ((IControlFlowGraphBuilderFactory)new ControlFlowGraphBuilderFactory()).Create().Build(body);
        var repository = new Repository();
        return new(method, new TypedStackValidator(repository, repository, repository, new StackTypeCompatibilityValidator()).Validate(graph));
    }

    private static MethodDefinitionModel Method(EntityKey key, CliTypeIdentity result, params CliTypeIdentity[] parameters) =>
        new(key, HostKey, "Method", true, new(result, [.. parameters]), 1);

    private static CilInstruction I(int offset, CilOperation operation) => new(offset, offset + 1, operation, new CilOperand.None());

    private static CilInstruction Call(int offset, EntityKey key) => new(offset, offset + 1, CilOperation.Call, new CilOperand.Entity(key));

    private static CilInstruction Field(int offset, EntityKey key) => new(offset, offset + 1, CilOperation.LoadFieldAddress, new CilOperand.Entity(key));

    private static CilInstruction Constant(int offset) => new(offset, offset + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0));

    private static CilInstruction Index(int offset, CilOperation operation, int index) => new(offset, offset + 1, operation, new CilOperand.Index(index));

    private static CilInstruction Branch(int offset, CilOperation operation, int target) => new(offset, offset + 1, operation, new CilOperand.BranchTarget(target));

    private sealed class RecordingContinuations(StackStorageContinuations result) : IStackStorageContinuationAnalyzer
    {
        public int Calls { get; private set; }

        public StackStorageContinuations Analyze(ManagedMethodBody method)
        {
            Calls++;
            return result;
        }
    }

    private sealed class Repository : ITypeRepository, IFieldRepository, IMethodRepository, IValueLayoutProvider
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(key, "Tests",
            key == PairKey ? "Pair" : key == OtherKey ? "Other" : "Host", key != HostKey, [], []);

        public FieldDefinitionModel GetField(EntityKey key) => new(key, key == PairField ? PairKey : OtherKey, "First", I4, false);

        public MethodDefinitionModel GetMethod(EntityKey key) => key == ProducerKey ? Method(key, CliTypeIdentity.ManagedByReference(Pair)) :
            key == OtherProducerKey ? Method(key, CliTypeIdentity.ManagedByReference(Other)) :
            key == ConsumeKey ? Method(key, Void, CliTypeIdentity.ManagedByReference(I4)) : Method(key, Void);

        public ValueLayout GetValueLayout(CliTypeIdentity type) => new(type, 8, 4, [4]);
    }
}
