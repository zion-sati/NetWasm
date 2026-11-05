using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class AddressTakenAggregatePromotionPlannerTests
{
    [Theory]
    [InlineData(CilOperation.Nop, true)]
    [InlineData(CilOperation.Pop, false)]
    [InlineData(CilOperation.Branch, true)]
    [InlineData(CilOperation.BranchIfTrue, false)]
    [InlineData(CilOperation.BranchIfFalse, false)]
    [InlineData(CilOperation.BranchIfEqual, false)]
    [InlineData(CilOperation.BranchIfNotEqual, false)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned, false)]
    [InlineData(CilOperation.BranchIfGreaterThanUnsigned, false)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned, false)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned, false)]
    [InlineData(CilOperation.BranchIfLessThanSigned, false)]
    [InlineData(CilOperation.BranchIfLessThanUnsigned, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualSigned, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualUnsigned, false)]
    [InlineData(CilOperation.StoreArgument, false)]
    [InlineData(CilOperation.StoreLocal, false)]
    [InlineData(CilOperation.StoreField, false)]
    [InlineData(CilOperation.StoreStaticField, false)]
    [InlineData(CilOperation.StoreObject, false)]
    [InlineData(CilOperation.CopyObject, false)]
    [InlineData(CilOperation.InitializeObject, false)]
    [InlineData(CilOperation.CopyBlock, false)]
    [InlineData(CilOperation.InitializeBlock, false)]
    [InlineData(CilOperation.InitializeArrayData, false)]
    [InlineData(CilOperation.StoreArrayElementReference, false)]
    [InlineData(CilOperation.StoreArrayElement, false)]
    [InlineData(CilOperation.StoreRectangularArrayElement, false)]
    [InlineData(CilOperation.Constrained, true)]
    [InlineData(CilOperation.Volatile, true)]
    [InlineData(CilOperation.Readonly, true)]
    [InlineData(CilOperation.Unaligned, true)]
    [InlineData(CilOperation.Break, true)]
    [InlineData(CilOperation.Throw, false)]
    [InlineData(CilOperation.Rethrow, false)]
    [InlineData(CilOperation.Leave, true)]
    [InlineData(CilOperation.EndFinally, false)]
    [InlineData(CilOperation.EndFilter, false)]
    [InlineData(CilOperation.Return, false)]
    public void ZeroProducingOperationsFollowTheirValidatedStackTransition(
        CilOperation operation, bool preservesStack)
    {
        var fixture = new Fixture();
        var steps = fixture.ProjectionSteps(6).ToList();
        steps.Add(Step(Fixture.LocalAddress(18, 0)));
        steps.Add(Step(Fixture.Instruction(19, operation),
            CliValueKind.ManagedAddress));
        steps.Add(preservesStack
            ? Step(Fixture.Instruction(20, CilOperation.Nop),
                CliValueKind.ManagedAddress)
            : Step(Fixture.Instruction(20, CilOperation.Nop)));
        if (preservesStack)
        {
            steps.Add(Step(Fixture.Instruction(21, CilOperation.Pop),
                CliValueKind.ManagedAddress));
        }

        Assert.Empty(fixture.Plan(fixture.Method([.. steps])).PromotedLocals);
    }

    [Fact]
    public void DuplicateCopiesTheValidatedStackOrigin()
    {
        var fixture = new Fixture();
        var steps = fixture.ProjectionSteps(6).Concat([
            Step(Fixture.Instruction(18, CilOperation.LoadInt32)),
            Step(Fixture.Instruction(19, CilOperation.Duplicate), CliValueKind.I4),
            Step(Fixture.Instruction(20, CilOperation.Pop),
                CliValueKind.I4, CliValueKind.I4),
            Step(Fixture.Instruction(21, CilOperation.Pop), CliValueKind.I4),
        ]).ToArray();

        Assert.Equal([0], fixture.Plan(fixture.Method(steps))
            .PromotedLocals.Keys.ToArray());
    }

    [Fact]
    public void SelectsSplitStorageWhenOverlappingProjectionsRequireIt()
    {
        var projections = new Dictionary<int, AddressTakenAggregateProjection>
        {
            [0] = new(0, 0, 4, CliValueKind.I4),
            [1] = new(0, 4, 4, CliValueKind.I4),
            [2] = new(0, 0, 8, CliValueKind.I8),
        };
        var operations = projections.Keys.ToDictionary(
            offset => offset,
            _ => CilOperation.LoadField);
        operations[2] = CilOperation.StoreField;

        var slots = AddressTakenAggregatePromotionPlanner.SelectStorageSlots(
            8, projections, operations, 0);

        Assert.Equal(
            [
                new AddressTakenAggregateStorageSlot(0, 4, CliValueKind.I4),
                new AddressTakenAggregateStorageSlot(4, 4, CliValueKind.I4),
            ],
            slots.ToArray());
    }

    [Fact]
    public void ZeroSizedStorageHasNoSlots()
    {
        var slots = AddressTakenAggregatePromotionPlanner.SelectStorageSlots(
            0, [], [], 0);

        Assert.Empty(slots);
    }

    [Fact]
    public void CandidateDiscoveryIgnoresUnreachableOutOfRangeAndNonAggregateLocals()
    {
        var fixture = new Fixture();
        var int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var steps = fixture.ProjectionSteps(6).ToArray();
        var method = fixture.Method([fixture.Aggregate, int32], steps);
        method = method with
        {
            Header = method.Header with
            {
                Instructions = method.Header.Instructions
                    .Add(Fixture.LocalAddress(99, 1))
                    .Add(Fixture.LocalAddress(100, 2)),
            },
        };

        var plan = fixture.Plan(method);

        Assert.Equal([0], plan.PromotedLocals.Keys.ToArray());
    }

    [Fact]
    public void ResynchronizesOriginsWhenValidatedEntryDepthChanges()
    {
        var fixture = new Fixture();
        var method = fixture.Method(
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Instruction(1, CilOperation.Nop),
                CliValueKind.ManagedAddress, CliValueKind.I4));

        Assert.Empty(fixture.Plan(method).PromotedLocals);
    }

    [Fact]
    public void FieldStackEdgeCasesDoNotCorruptValidProjectionTraffic()
    {
        var fixture = new Fixture();
        var method = fixture.FieldStackEdgeMethod();

        var plan = fixture.Plan(method);

        Assert.Equal([0], plan.PromotedLocals.Keys.ToArray());
        Assert.Equal(6, plan.Projections.Count);
    }

    [Fact]
    public void InvalidStoreProjectionRejectsItsAggregateCandidate()
    {
        var fixture = new Fixture();
        var invalidField = fixture.Field32 with
        {
            DeclaringType = CliTypeIdentity.FromStackKind(CliValueKind.I4),
        };
        var steps = fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Instruction(19, CilOperation.LoadInt32),
                CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(20, CilOperation.StoreField,
                    new CilOperand.FieldInstance(invalidField)),
                CliValueKind.ManagedAddress, CliValueKind.I4),
        ]).ToArray();

        Assert.Empty(fixture.Plan(fixture.Method(steps)).PromotedLocals);
    }

    [Fact]
    public void ProjectionFromANonCandidateLocalDoesNotRejectTheValidCandidate()
    {
        var fixture = new Fixture();
        var int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var steps = fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 1)),
            Step(Fixture.Instruction(19, CilOperation.LoadField,
                    new CilOperand.FieldInstance(fixture.Field32)),
                CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(20, CilOperation.Pop), CliValueKind.I4),
        ]).ToArray();

        var plan = fixture.Plan(fixture.Method([fixture.Aggregate, int32], steps));

        Assert.Equal([0], plan.PromotedLocals.Keys.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronizationFlowsThroughFallthroughAndLeaveExits(bool leave)
    {
        var fixture = new Fixture();
        var target = Fixture.Target(fixture.ByRefAggregate);

        var plan = fixture.Plan(fixture.LinearSynchronizationMethod(target, leave),
            target, ManagedByRefMethodEffects.Empty);

        Assert.Equal([0], plan.CallSynchronizations[22].LocalIndices.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingCallSiteOrEffectFactsRejectTheCandidate(int scenario)
    {
        var fixture = new Fixture();
        var target = Fixture.Target(fixture.ByRefAggregate);
        var method = fixture.Method(fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Call(19, target), CliValueKind.ManagedAddress),
        ]).ToArray());
        var request = fixture.Request(method, target, ManagedByRefMethodEffects.Empty);
        request = scenario == 0
            ? request with { CallSites = ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Empty }
            : request with { ByRefEffects = ManagedByRefEffects.Empty };

        Assert.Empty(fixture.CreatePlanner().Plan(request).PromotedLocals);
    }

    [Fact]
    public void NonVoidCallReturnHasNoAggregateOrigin()
    {
        var fixture = new Fixture();
        var target = Fixture.Target(CliValueKind.I4, fixture.ByRefAggregate);
        var method = fixture.Method(fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Call(19, target), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(20, CilOperation.Pop), CliValueKind.I4),
        ]).ToArray());

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Equal([0], plan.PromotedLocals.Keys.ToArray());
    }

    [Fact]
    public void UnknownOperationsRejectTerminalAndInvalidStackOrigins()
    {
        var fixture = new Fixture();
        var terminal = fixture.Method(fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Instruction(19, CilOperation.LoadObject,
                    new CilOperand.TypeIdentity(fixture.Aggregate)),
                CliValueKind.ManagedAddress),
        ]).ToArray());
        var invalid = fixture.Method(fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Instruction(19, CilOperation.Pop), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(20, CilOperation.Nop),
                CliValueKind.ManagedAddress, CliValueKind.I4),
        ]).ToArray());

        Assert.Empty(fixture.Plan(terminal).PromotedLocals);
        Assert.Empty(fixture.Plan(invalid).PromotedLocals);
    }

    [Theory]
    [InlineData(CliValueKind.Void)]
    [InlineData(CliValueKind.I4)]
    public void IndirectCallsRejectAggregateOrigins(CliValueKind returnType)
    {
        var fixture = new Fixture();
        var signature = new MethodSignatureModel(
            CliTypeIdentity.FromStackKind(returnType),
            [fixture.ByRefAggregate]);
        var steps = fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Instruction(19, CilOperation.CallIndirect,
                    new CilOperand.CallSite(signature)), CliValueKind.ManagedAddress),
        ]).ToList();
        steps.Add(returnType == CliValueKind.Void
            ? Step(Fixture.Instruction(20, CilOperation.Nop))
            : Step(Fixture.Instruction(20, CilOperation.Pop), CliValueKind.I4));

        Assert.Empty(fixture.Plan(fixture.Method([.. steps])).PromotedLocals);
    }

    [Fact]
    public void PlansOverlappingIntegerProjectionsAndWritingCallSynchronization()
    {
        var fixture = new Fixture();
        var target = Fixture.Target(fixture.ByRefAggregate);
        var steps = fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Call(19, target), CliValueKind.ManagedAddress),
        ]).ToArray();
        var method = fixture.Method(steps);

        var plan = fixture.Plan(
            method,
            target,
            new ManagedByRefMethodEffects([0], [], []));

        Assert.Equal(
            [
                new AddressTakenAggregateStorageSlot(0, 4, CliValueKind.I4),
                new AddressTakenAggregateStorageSlot(4, 8, CliValueKind.I8),
            ],
            plan.PromotedLocals[0].StorageSlots.ToArray());
        Assert.Equal(12, plan.PromotedLocals[0].Size);
        Assert.Equal(6, plan.Projections.Count);
        Assert.Contains(plan.Projections.Values, projection =>
            projection.StackKind == CliValueKind.I4 &&
            projection.ByteOffset == 0 &&
            projection.Size == 4);
        Assert.Contains(plan.Projections.Values, projection =>
            projection.StackKind == CliValueKind.I8 &&
            projection.ByteOffset == 4 &&
            projection.Size == 8);
        Assert.Empty(plan.CallSynchronizations[19].LocalIndices);
        Assert.Equal([0], plan.CallSynchronizations[19].ReloadLocalIndices.ToArray());
    }

    [Fact]
    public void ReadOnlyCallDoesNotReloadAggregateLanes()
    {
        var fixture = new Fixture();
        var target = Fixture.Target(fixture.ByRefAggregate);
        var method = fixture.Method(fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Call(19, target), CliValueKind.ManagedAddress),
        ]).ToArray());

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.DoesNotContain(19, plan.CallSynchronizations.Keys);
    }

    [Fact]
    public void FlushesDirtyLanesBeforeAReadOnlyCall()
    {
        var fixture = new Fixture();
        var target = Fixture.Target(fixture.ByRefAggregate);
        var method = fixture.Method(fixture.ProjectionSteps(6).Concat(
            fixture.StoreProjectionSteps(18)).Concat([
                Step(Fixture.LocalAddress(21, 0)),
                Step(Fixture.Call(22, target), CliValueKind.ManagedAddress),
            ]).ToArray());

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Equal([0], plan.CallSynchronizations[22].LocalIndices.ToArray());
        Assert.Empty(plan.CallSynchronizations[22].ReloadLocalIndices);
    }

    [Fact]
    public void FlushesWhenOnlyOneMergedPathIsDirty()
    {
        var fixture = new Fixture();
        var target = Fixture.Target(fixture.ByRefAggregate);
        var method = fixture.BranchMergeMethod(target);

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Equal([0], plan.CallSynchronizations[51].LocalIndices.ToArray());
    }

    [Fact]
    public void FixedPointFindsLoopCarriedDirtyStateBeforeCall()
    {
        var fixture = new Fixture();
        var target = Fixture.Target(fixture.ByRefAggregate);
        var method = fixture.LoopCarriedDirtyMethod(target);

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Equal([0], plan.CallSynchronizations[19].LocalIndices.ToArray());
    }

    [Fact]
    public void PlansDefinitionFieldOperandsForTheAggregateOwner()
    {
        var fixture = new Fixture();
        var method = fixture.Method(fixture.ProjectionSteps(6, useDefinition: true)
            .ToArray());

        var plan = fixture.Plan(method);

        Assert.Equal(6, plan.Projections.Count);
    }

    [Fact]
    public void RejectsAggregateWithoutEnoughProjectionBenefit()
    {
        var fixture = new Fixture();
        var method = fixture.Method(fixture.ProjectionSteps(3).ToArray());

        Assert.Empty(fixture.Plan(method).PromotedLocals);
    }

    [Theory]
    [InlineData(Rejection.Disabled)]
    [InlineData(Rejection.ReferenceBearing)]
    [InlineData(Rejection.ByReferenceBearing)]
    [InlineData(Rejection.TooSmall)]
    [InlineData(Rejection.TooLarge)]
    [InlineData(Rejection.Misaligned)]
    [InlineData(Rejection.UnsupportedField)]
    [InlineData(Rejection.EscapedCall)]
    [InlineData(Rejection.ReturnedAlias)]
    [InlineData(Rejection.ExceptionRegion)]
    [InlineData(Rejection.WrongFieldOwner)]
    [InlineData(Rejection.StaticField)]
    public void RejectsUnsafeOrUnsupportedCandidates(Rejection rejection)
    {
        var fixture = new Fixture(
            references: rejection == Rejection.ReferenceBearing,
            byReferences: rejection == Rejection.ByReferenceBearing,
            size: rejection switch
            {
                Rejection.TooSmall => 4,
                Rejection.TooLarge => 20,
                Rejection.Misaligned => 10,
                _ => 12,
            },
            unsupportedField: rejection == Rejection.UnsupportedField,
            wrongFieldOwner: rejection == Rejection.WrongFieldOwner,
            staticField: rejection == Rejection.StaticField);
        var target = Fixture.Target(fixture.ByRefAggregate);
        var method = fixture.Method(fixture.ProjectionSteps(6).Concat([
            Step(Fixture.LocalAddress(18, 0)),
            Step(Fixture.Call(19, target), CliValueKind.ManagedAddress),
        ]).ToArray());
        if (rejection == Rejection.ExceptionRegion)
        {
            var groupId = new StructuredExceptionGroupId(0);
            method = method with
            {
                ExceptionGroups = method.ExceptionGroups.Add(
                    groupId,
                    new(groupId, null, 0, 20, [], [], [], null, null)),
            };
        }
        var effects = rejection switch
        {
            Rejection.EscapedCall => new ManagedByRefMethodEffects([], [0], []),
            Rejection.ReturnedAlias => new ManagedByRefMethodEffects([], [], [0]),
            _ => ManagedByRefMethodEffects.Empty,
        };

        var plan = fixture.Plan(
            method,
            target,
            effects,
            enabled: rejection != Rejection.Disabled);

        Assert.Empty(plan.PromotedLocals);
    }

    [Fact]
    public void RejectsNullDependenciesAndRequestMembers()
    {
        var fixture = new Fixture();
        var target = Fixture.Target(fixture.ByRefAggregate);
        var method = fixture.Method(fixture.ProjectionSteps(6).ToArray());
        var request = fixture.Request(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Throws<ArgumentNullException>(() =>
            new AddressTakenAggregatePromotionPlanner(
                null!, fixture, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() =>
            new AddressTakenAggregatePromotionPlanner(
                fixture, null!, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() =>
            new AddressTakenAggregatePromotionPlanner(
                fixture, fixture, null!, fixture));
        Assert.Throws<ArgumentNullException>(() =>
            new AddressTakenAggregatePromotionPlanner(
                fixture, fixture, fixture, null!));
        var planner = fixture.CreatePlanner();
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { Method = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { CallSites = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { ByRefEffects = null! }));
    }

    public enum Rejection
    {
        Disabled,
        ReferenceBearing,
        ByReferenceBearing,
        TooSmall,
        TooLarge,
        Misaligned,
        UnsupportedField,
        EscapedCall,
        ReturnedAlias,
        ExceptionRegion,
        WrongFieldOwner,
        StaticField,
    }

    private static InstructionStep Step(
        CilInstruction instruction,
        params CliValueKind[] entryStack) => new(instruction, [.. entryStack]);

    private readonly record struct InstructionStep(
        CilInstruction Instruction,
        ImmutableArray<CliValueKind> EntryStack);

    private sealed class Fixture :
        IFieldRepository,
        ITypeDefinitionResolver,
        IValueLayoutProvider,
        IInstanceFieldLayoutProvider
    {
        private static readonly AssemblyIdentity Assembly = new("aggregate-promotion-tests");
        private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
        private static readonly CliTypeIdentity Container =
            CliTypeIdentity.Named(Assembly, "Tests", "Container", false);
        private readonly bool _references;
        private readonly bool _byReferences;
        private readonly int _size;
        private readonly bool _unsupportedField;
        private readonly MethodDefinitionModel _body;
        private readonly FieldDefinitionModel _i4Field;
        private readonly FieldDefinitionModel _i8Field;

        public Fixture(
            bool references = false,
            bool byReferences = false,
            int size = 12,
            bool unsupportedField = false,
            bool wrongFieldOwner = false,
            bool staticField = false)
        {
            _references = references;
            _byReferences = byReferences;
            _size = size;
            _unsupportedField = unsupportedField;
            Aggregate = CliTypeIdentity.Named(
                Assembly,
                "Tests",
                "Buffer",
                isValueType: true);
            ByRefAggregate = CliTypeIdentity.ManagedByReference(Aggregate);
            _i4Field = new(
                new(Assembly, 0x04000001),
                TypeKey,
                "Low",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                staticField);
            _i8Field = new(
                new(Assembly, 0x04000002),
                TypeKey,
                "High",
                CliTypeIdentity.FromStackKind(
                    unsupportedField ? CliValueKind.F8 : CliValueKind.I8),
                false);
            var fieldOwner = wrongFieldOwner
                ? CliTypeIdentity.Named(
                    Assembly,
                    "Tests",
                    "OtherBuffer",
                    isValueType: true)
                : Aggregate;
            Field32 = new(_i4Field, fieldOwner, _i4Field.SignatureType);
            Field64 = new(_i8Field, Aggregate, _i8Field.SignatureType);
            _body = Definition(1, "Body", true, MethodSignatureModel.Create(CliValueKind.Void));
            Caller = new ManagedMethodIdentity("Tests.Body");
        }

        public CliTypeIdentity Aggregate { get; }

        public CliTypeIdentity ByRefAggregate { get; }

        public FieldInstanceModel Field32 { get; }

        public FieldInstanceModel Field64 { get; }

        public ManagedMethodIdentity Caller { get; }

        public IEnumerable<InstructionStep> ProjectionSteps(
            int count,
            int startOffset = 0,
            bool useDefinition = false)
        {
            for (var index = 0; index < count; index++)
            {
                var offset = startOffset + index * 3;
                var field = index % 2 == 0 ? Field32 : Field64;
                yield return Step(LocalAddress(offset, 0));
                yield return Step(
                    Instruction(
                        offset + 1,
                        CilOperation.LoadField,
                        useDefinition
                            ? new CilOperand.Entity(field.Definition.Key)
                            : new CilOperand.FieldInstance(field)),
                    CliValueKind.ManagedAddress);
                yield return Step(
                    Instruction(offset + 2, CilOperation.Pop),
                    field.FieldType.StackKind);
            }
        }

        public IEnumerable<InstructionStep> StoreProjectionSteps(int offset)
        {
            yield return Step(LocalAddress(offset, 0));
            yield return Step(
                Instruction(
                    offset + 1,
                    CilOperation.LoadInt32,
                    new CilOperand.ConstantI4(1)),
                CliValueKind.ManagedAddress);
            yield return Step(
                Instruction(
                    offset + 2,
                    CilOperation.StoreField,
                    new CilOperand.FieldInstance(Field32)),
                CliValueKind.ManagedAddress,
                CliValueKind.I4);
        }

        public StructuredMethod BranchMergeMethod(MethodInstanceModel target)
        {
            var dirty = ProjectionSteps(6, startOffset: 10)
                .Concat(StoreProjectionSteps(28))
                .ToArray();
            var clean = new[]
            {
                Step(Instruction(40, CilOperation.Nop)),
            };
            var join = new[]
            {
                Step(LocalAddress(50, 0)),
                Step(Call(51, target), CliValueKind.ManagedAddress),
            };
            var entryId = new StructuredBlockId(0);
            var dirtyId = new StructuredBlockId(1);
            var cleanId = new StructuredBlockId(2);
            var joinId = new StructuredBlockId(3);
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>
                .Empty
                .Add(entryId, Block(
                    entryId,
                    0,
                    [],
                    new StructuredConditionalExit(
                        new(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null),
                        dirtyId,
                        cleanId)))
                .Add(dirtyId, Block(
                    dirtyId,
                    10,
                    dirty,
                    new StructuredBranchExit(31, joinId)))
                .Add(cleanId, Block(
                    cleanId,
                    40,
                    clean,
                    new StructuredBranchExit(41, joinId)))
                .Add(joinId, Block(
                    joinId,
                    50,
                    join,
                    new StructuredFallthroughExit(null)));
            return GraphMethod(entryId, blocks, dirty.Concat(clean).Concat(join));
        }

        public StructuredMethod LoopCarriedDirtyMethod(MethodInstanceModel target)
        {
            var loop = ProjectionSteps(6).Concat([
                Step(LocalAddress(18, 0)),
                Step(Call(19, target), CliValueKind.ManagedAddress),
            ]).Concat(StoreProjectionSteps(20)).ToArray();
            var exit = new[]
            {
                Step(Instruction(30, CilOperation.Nop)),
            };
            var loopId = new StructuredBlockId(0);
            var exitId = new StructuredBlockId(1);
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>
                .Empty
                .Add(loopId, Block(
                    loopId,
                    0,
                    loop,
                    new StructuredConditionalExit(
                        new(23, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null),
                        loopId,
                        exitId)))
                .Add(exitId, Block(
                    exitId,
                    30,
                    exit,
                    new StructuredFallthroughExit(null)));
            return GraphMethod(loopId, blocks, loop.Concat(exit));
        }

        public StructuredMethod LinearSynchronizationMethod(
            MethodInstanceModel target, bool leave)
        {
            var first = ProjectionSteps(6).Concat(StoreProjectionSteps(18)).ToArray();
            var second = new[]
            {
                Step(LocalAddress(21, 0)),
                Step(Call(22, target), CliValueKind.ManagedAddress),
            };
            var firstId = new StructuredBlockId(0);
            var secondId = new StructuredBlockId(1);
            StructuredBlockExit firstExit = leave
                ? new StructuredLeaveExit(20, secondId)
                : new StructuredFallthroughExit(secondId);
            var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>
                .Empty
                .Add(firstId, Block(firstId, 0, first, firstExit))
                .Add(secondId, Block(secondId, 21, second,
                    new StructuredFallthroughExit(null)));
            return GraphMethod(firstId, blocks, first.Concat(second));
        }

        public StructuredMethod FieldStackEdgeMethod()
        {
            var steps = ProjectionSteps(6).Concat([
                Step(Instruction(18, CilOperation.LoadField,
                    new CilOperand.FieldInstance(Field32))),
                Step(Instruction(19, CilOperation.Pop), CliValueKind.I4),
                Step(Instruction(20, CilOperation.LoadInt32)),
                Step(Instruction(21, CilOperation.LoadField,
                        new CilOperand.FieldInstance(Field32)), CliValueKind.I4),
                Step(Instruction(22, CilOperation.Pop), CliValueKind.I4),
                Step(Instruction(23, CilOperation.StoreField,
                    new CilOperand.FieldInstance(Field32))),
                Step(Instruction(24, CilOperation.LoadInt32)),
                Step(Instruction(25, CilOperation.LoadInt32), CliValueKind.I4),
                Step(Instruction(26, CilOperation.StoreField,
                        new CilOperand.FieldInstance(Field32)),
                    CliValueKind.I4, CliValueKind.I4),
            ]).ToArray();
            return Method(steps);
        }

        public static MethodInstanceModel Target(params CliTypeIdentity[] parameters) =>
            Target(CliValueKind.Void, parameters);

        public static MethodInstanceModel Target(
            CliValueKind returnType,
            params CliTypeIdentity[] parameters)
        {
            var signature = new MethodSignatureModel(
                CliTypeIdentity.FromStackKind(returnType),
                [.. parameters]);
            var definition = Definition(2, "Target", true, signature);
            return new(definition, Container, [], signature);
        }

        public StructuredMethod Method(params InstructionStep[] steps)
            => Method([Aggregate], steps);

        public StructuredMethod Method(
            ImmutableArray<CliTypeIdentity> locals,
            params InstructionStep[] steps)
        {
            var instructions = steps.Select(step => step.Instruction).ToImmutableArray();
            var blockId = new StructuredBlockId(0);
            var block = new StructuredBlockDefinition(
                blockId,
                instructions[0].Offset,
                instructions,
                steps[0].EntryStack,
                new StructuredFallthroughExit(null))
            {
                EndOffset = instructions[^1].NextOffset,
            };
            return new(
                new StructuredMethodHeader(
                    _body,
                    null,
                    8,
                    [.. locals.Select(type => type.StackKind)],
                    locals,
                    instructions),
                blockId,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                    .Add(blockId, block),
                new StructuredSequence([
                    new StructuredCode(new(blockId, StructuredBlockRole.Owner)),
                ]),
                [],
                ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
                steps.ToImmutableDictionary(
                    step => step.Instruction.Offset,
                    step => step.EntryStack));
        }

        private StructuredMethod GraphMethod(
            StructuredBlockId entry,
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> blocks,
            IEnumerable<InstructionStep> steps)
        {
            var materialized = steps.ToImmutableArray();
            return new(
                new StructuredMethodHeader(
                    _body,
                    null,
                    8,
                    [CliValueKind.ValueType],
                    [Aggregate],
                    [.. materialized.Select(step => step.Instruction)
                        .OrderBy(instruction => instruction.Offset)]),
                entry,
                blocks,
                StructuredSequence.Empty,
                [],
                ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
                materialized.ToImmutableDictionary(
                    step => step.Instruction.Offset,
                    step => step.EntryStack));
        }

        private static StructuredBlockDefinition Block(
            StructuredBlockId id,
            int startOffset,
            IEnumerable<InstructionStep> steps,
            StructuredBlockExit exit)
        {
            var instructions = steps.Select(step => step.Instruction).ToImmutableArray();
            return new(id, startOffset, instructions, [], exit)
            {
                EndOffset = instructions.IsEmpty
                    ? startOffset
                    : instructions[^1].NextOffset,
            };
        }

        public AddressTakenAggregatePromotionPlan Plan(
            StructuredMethod method,
            MethodInstanceModel? target = null,
            ManagedByRefMethodEffects? effects = null,
            bool enabled = true)
        {
            target ??= Target(ByRefAggregate);
            effects ??= ManagedByRefMethodEffects.Empty;
            return CreatePlanner().Plan(Request(method, target, effects, enabled));
        }

        public AddressTakenAggregatePromotionRequest Request(
            StructuredMethod method,
            MethodInstanceModel target,
            ManagedByRefMethodEffects effects,
            bool enabled = true)
        {
            var targetIdentity = new ManagedMethodIdentity(target.CanonicalName);
            var calls = method.Header.Instructions
                .Where(instruction => instruction.Operation == CilOperation.Call)
                .Select(instruction => new ManagedCallSite(
                    new(Caller, instruction.Offset),
                    ManagedCallOperation.Direct,
                    targetIdentity,
                    target,
                    null))
                .ToImmutableDictionary(call => call.Key);
            return new(
                method,
                Caller,
                calls,
                new(ImmutableDictionary<string, ManagedByRefMethodEffects>.Empty.Add(
                    targetIdentity.CanonicalName,
                    effects)),
                enabled);
        }

        public AddressTakenAggregatePromotionPlanner CreatePlanner() =>
            new(this, this, this, this);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            TypeKey,
            "Tests",
            "Buffer",
            IsValueType: true,
            [_i4Field.Key, _i8Field.Key],
            []);

        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            new(type, _size, 4, _references ? [0] : [])
            {
                ByReferenceOffsets = _byReferences ? [0] : [],
            };

        public FieldDefinitionModel GetField(EntityKey key) =>
            key == _i4Field.Key ? _i4Field : _i8Field;

        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            GetFieldLayout(field.Definition.Key);

        public FieldLayout GetFieldLayout(EntityKey field) =>
            field == _i4Field.Key
                ? new(0) { Size = 4, Type = _i4Field.SignatureType }
                : new(4) { Size = 8, Type = _i8Field.SignatureType };

        public static CilInstruction LocalAddress(int offset, int local) =>
            Instruction(offset, CilOperation.LoadLocalAddress, new CilOperand.Index(local));

        public static CilInstruction Call(int offset, MethodInstanceModel target) =>
            Instruction(offset, CilOperation.Call, new CilOperand.MethodInstance(target));

        public static CilInstruction Instruction(
            int offset,
            CilOperation operation,
            CilOperand? operand = null) => new(
                offset,
                offset + 1,
                operation,
                operand ?? new CilOperand.None());

        private static MethodDefinitionModel Definition(
            int token,
            string name,
            bool isStatic,
            MethodSignatureModel signature) => new(
            new(Assembly, 0x06000000 + token),
            TypeKey,
            name,
            isStatic,
            signature,
            1);
    }
}
