using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class AddressTakenScalarPromotionPlannerTests
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
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var steps = new List<InstructionStep>
        {
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Instruction(1, operation), CliValueKind.ManagedAddress),
            preservesStack
                ? Step(Fixture.Instruction(2, CilOperation.Nop), CliValueKind.ManagedAddress)
                : Step(Fixture.Instruction(2, CilOperation.Nop)),
        };
        if (preservesStack)
        {
            steps.Add(Step(Fixture.Instruction(3, CilOperation.Pop),
                CliValueKind.ManagedAddress));
        }

        Assert.Empty(fixture.Plan(fixture.Method([fixture.Int32], [.. steps]), target,
            ManagedByRefMethodEffects.Empty).PromotedLocals);
    }

    [Fact]
    public void IgnoresUnreachableAndOutOfRangeAddressCandidates()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);

        var reachable = fixture.Method(
            [fixture.Int32, fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress));
        reachable = reachable with
        {
            Header = reachable.Header with
            {
                Instructions = reachable.Header.Instructions.Add(Fixture.LocalAddress(99, 1)),
            },
        };
        Assert.Equal([0], fixture.Plan(reachable, target,
            ManagedByRefMethodEffects.Empty).PromotedLocals.Keys);

        var outOfRange = fixture.Method([], Step(Fixture.LocalAddress(0, 0)));
        Assert.Same(AddressTakenScalarPromotionPlan.Empty,
            fixture.Plan(outOfRange, target, ManagedByRefMethodEffects.Empty));
    }

    [Fact]
    public void IgnoresAddressOfUnpromotableLocalBesideAPromotedLocal()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var reference = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
        var method = fixture.Method(
            [fixture.Int32, reference],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress),
            Step(Fixture.LocalAddress(2, 1)),
            Step(Fixture.Instruction(3, CilOperation.Pop), CliValueKind.ManagedAddress));

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Equal([0], plan.PromotedLocals.Keys.ToArray());
    }

    [Fact]
    public void RejectsOriginsAfterEntryStackResetOrInvalidConsumption()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);

        Assert.Empty(fixture.Plan(fixture.EntryStackResetMethod(), target,
            ManagedByRefMethodEffects.Empty).PromotedLocals);
        Assert.Empty(fixture.Plan(fixture.InvalidConsumptionMethod(), target,
            ManagedByRefMethodEffects.Empty).PromotedLocals);
    }

    [Fact]
    public void MergesRepeatedCallSynchronizationAtTheSameInstruction()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.RepeatedBlockCallMethod(target);

        var plan = fixture.Plan(method, target, new ManagedByRefMethodEffects([0], [], []));

        var synchronization = Assert.Single(plan.CallSynchronizations).Value;
        Assert.Equal([0], synchronization.LocalIndices.ToArray());
        Assert.Equal([0], synchronization.ReloadLocalIndices.ToArray());
    }

    [Fact]
    public void NonVoidDirectCallsPreservePromotionAndDiscardTheReturnedValueOrigin()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.I4, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(2, CilOperation.Pop), CliValueKind.I4));

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Equal([0], plan.PromotedLocals.Keys.ToArray());
        Assert.Equal([0], plan.CallSynchronizations[1].LocalIndices.ToArray());
    }

    [Theory]
    [InlineData(CliValueKind.Void)]
    [InlineData(CliValueKind.I4)]
    public void IndirectCallsRejectOwnedOriginsForVoidAndValueReturns(CliValueKind returnType)
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var signature = new MethodSignatureModel(
            CliTypeIdentity.FromStackKind(returnType),
            [fixture.ByRefInt32]);
        var steps = new List<InstructionStep>
        {
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Instruction(1, CilOperation.CallIndirect,
                new CilOperand.CallSite(signature)), CliValueKind.ManagedAddress),
        };
        if (returnType == CliValueKind.Void)
        {
            steps.Add(Step(Fixture.Instruction(2, CilOperation.Nop)));
        }
        else
        {
            steps.Add(Step(Fixture.Instruction(2, CilOperation.Pop), returnType));
        }

        Assert.Empty(fixture.Plan(fixture.Method([fixture.Int32], [.. steps]), target,
            ManagedByRefMethodEffects.Empty).PromotedLocals);
    }

    [Fact]
    public void PlansSynchronizationWithoutReloadForReadOnlyDirectCall()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress));

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Equal(fixture.Int32, plan.PromotedLocals[0]);
        Assert.Equal([0], plan.CallSynchronizations[1].LocalIndices.ToArray());
        Assert.Empty(plan.CallSynchronizations[1].ReloadLocalIndices);
    }

    [Fact]
    public void PlansReloadForWrittenDirectCallArgument()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress));

        var plan = fixture.Plan(
            method,
            target,
            new ManagedByRefMethodEffects([0], [], []));

        Assert.Equal([0], plan.CallSynchronizations[1].LocalIndices.ToArray());
        Assert.Equal([0], plan.CallSynchronizations[1].ReloadLocalIndices.ToArray());
    }

    [Fact]
    public void DeduplicatesSameLocalPassedToMultipleArguments()
    {
        var fixture = new Fixture();
        var target = fixture.Target(
            CliValueKind.Void,
            fixture.ByRefInt32,
            fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Instruction(1, CilOperation.Duplicate), CliValueKind.ManagedAddress),
            Step(
                Fixture.Call(2, target),
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress));

        var plan = fixture.Plan(
            method,
            target,
            new ManagedByRefMethodEffects([1], [], []));

        Assert.Equal([0], plan.CallSynchronizations[2].LocalIndices.ToArray());
        Assert.Equal([0], plan.CallSynchronizations[2].ReloadLocalIndices.ToArray());
    }

    [Fact]
    public void PreservesAddressOriginWhileLaterArgumentsAreEvaluated()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32, fixture.Int32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Instruction(1, CilOperation.LoadInt32), CliValueKind.ManagedAddress),
            Step(
                Fixture.Call(2, target),
                CliValueKind.ManagedAddress,
                CliValueKind.I4));

        var plan = fixture.Plan(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Equal([0], plan.CallSynchronizations[2].LocalIndices.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RejectsEscapedAndReturnedAliases(int scenario)
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress));
        var effects = scenario == 0
            ? new ManagedByRefMethodEffects([], [0], [])
            : new ManagedByRefMethodEffects([], [], [0]);

        Assert.Empty(fixture.Plan(method, target, effects).PromotedLocals);
    }

    [Fact]
    public void RejectsVirtualCallUse()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(
                Fixture.Instruction(
                    1,
                    CilOperation.CallVirtual,
                    new CilOperand.MethodInstance(target)),
                CliValueKind.ManagedAddress));

        Assert.Empty(fixture.Plan(method, target, ManagedByRefMethodEffects.Empty)
            .PromotedLocals);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RejectsMissingCanonicalCallAndEffectFacts(int scenario)
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress));
        var request = fixture.Request(method, target, ManagedByRefMethodEffects.Empty);
        request = scenario == 0
            ? request with { CallSites = Lookups.Of(ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Empty) }
            : request with { ByRefEffects = Lookups.Of(ManagedByRefEffects.Empty.Methods) };

        Assert.Empty(new AddressTakenScalarPromotionPlanner().Plan(request)
            .PromotedLocals);
    }

    [Fact]
    public void MapsInstanceReceiverAndParameterEffectIndices()
    {
        var fixture = new Fixture();
        var target = fixture.InstanceTarget(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.Instruction(0, CilOperation.LoadNull)),
            Step(Fixture.LocalAddress(1, 0), CliValueKind.ManagedReference),
            Step(
                Fixture.Call(2, target),
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress));

        var plan = fixture.Plan(
            method,
            target,
            new ManagedByRefMethodEffects([1], [], []));

        Assert.Equal([0], plan.CallSynchronizations[2].LocalIndices.ToArray());
        Assert.Equal([0], plan.CallSynchronizations[2].ReloadLocalIndices.ToArray());
    }

    [Fact]
    public void RejectsUnsupportedAddressConsumer()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(
                Fixture.Instruction(
                    1,
                    CilOperation.LoadObject,
                    new CilOperand.TypeIdentity(fixture.Int32)),
                CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(2, CilOperation.Pop), CliValueKind.I4));

        Assert.Empty(fixture.Plan(method, target, ManagedByRefMethodEffects.Empty)
            .PromotedLocals);
    }

    [Fact]
    public void RejectsAddressThatCrossesABlockBoundary()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)));

        Assert.Empty(fixture.Plan(method, target, ManagedByRefMethodEffects.Empty)
            .PromotedLocals);
    }

    [Fact]
    public void RejectsManagedReferenceLocal()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)],
            Step(Fixture.LocalAddress(0, 0)));

        Assert.Empty(fixture.Plan(method, target, ManagedByRefMethodEffects.Empty)
            .PromotedLocals);
    }

    [Fact]
    public void DisabledPolicyReturnsEmptyPlan()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress));

        Assert.Same(
            AddressTakenScalarPromotionPlan.Empty,
            fixture.Plan(method, target, ManagedByRefMethodEffects.Empty, enabled: false));
    }

    [Fact]
    public void ExceptionRegionsReturnEmptyPlan()
    {
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method(
            [fixture.Int32],
            Step(Fixture.LocalAddress(0, 0)),
            Step(Fixture.Call(1, target), CliValueKind.ManagedAddress));
        var groupId = new StructuredExceptionGroupId(0);
        method = method with
        {
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId,
                StructuredExceptionGroup>.Empty.Add(
                    groupId,
                    new(groupId, null, 0, 2, [], [], [], null, null)),
        };

        Assert.Same(
            AddressTakenScalarPromotionPlan.Empty,
            fixture.Plan(method, target, ManagedByRefMethodEffects.Empty));
    }

    [Fact]
    public void RejectsNullRequestAndRequestDependencies()
    {
        var planner = new AddressTakenScalarPromotionPlanner();
        var fixture = new Fixture();
        var target = fixture.Target(CliValueKind.Void, fixture.ByRefInt32);
        var method = fixture.Method([fixture.Int32], Step(Fixture.LocalAddress(0, 0)));
        var valid = fixture.Request(method, target, ManagedByRefMethodEffects.Empty);

        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(valid with { Method = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(valid with { CallSites = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(valid with { ByRefEffects = null! }));
    }

    private static InstructionStep Step(
        CilInstruction instruction,
        params CliValueKind[] entryStack) => new(instruction, [.. entryStack]);

    private readonly record struct InstructionStep(
        CilInstruction Instruction,
        ImmutableArray<CliValueKind> EntryStack);

    private sealed class Fixture
    {
        private readonly MethodDefinitionModel _body;
        private int _nextMethodToken = 2;

        public Fixture()
        {
            Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
            ByRefInt32 = CliTypeIdentity.ManagedByReference(Int32);
            _body = Definition(1, "Body", true, MethodSignatureModel.Create(CliValueKind.Void));
            Caller = new ManagedMethodIdentity("Tests.Body");
        }

        public CliTypeIdentity Int32 { get; }
        public CliTypeIdentity ByRefInt32 { get; }
        public ManagedMethodIdentity Caller { get; }

        public MethodInstanceModel Target(
            CliValueKind returnType,
            params CliTypeIdentity[] parameterTypes)
            => Target(returnType, true, parameterTypes);

        public MethodInstanceModel InstanceTarget(
            CliValueKind returnType,
            params CliTypeIdentity[] parameterTypes)
            => Target(returnType, false, parameterTypes);

        private MethodInstanceModel Target(
            CliValueKind returnType,
            bool isStatic,
            params CliTypeIdentity[] parameterTypes)
        {
            var signature = new MethodSignatureModel(
                CliTypeIdentity.FromStackKind(returnType),
                [.. parameterTypes]);
            var definition = Definition(
                _nextMethodToken++,
                "Target",
                isStatic,
                signature);
            return new(definition, Container, [], signature);
        }

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

        public StructuredMethod EntryStackResetMethod() => Method(
            [Int32],
            Step(LocalAddress(0, 0)),
            Step(Instruction(1, CilOperation.Nop),
                CliValueKind.ManagedAddress, CliValueKind.I4));

        public StructuredMethod InvalidConsumptionMethod() => Method(
            [Int32],
            Step(LocalAddress(0, 0)),
            Step(Instruction(1, CilOperation.Pop), CliValueKind.ManagedAddress),
            Step(Instruction(2, CilOperation.Nop),
                CliValueKind.ManagedAddress, CliValueKind.I4));

        public StructuredMethod RepeatedBlockCallMethod(MethodInstanceModel target)
        {
            var method = Method(
                [Int32],
                Step(LocalAddress(0, 0)),
                Step(Call(1, target), CliValueKind.ManagedAddress));
            var original = method.Blocks[method.EntryBlock];
            var duplicateId = new StructuredBlockId(1);
            var duplicate = original with { Id = duplicateId, StartOffset = 1 };
            return method with { Blocks = method.Blocks.Add(duplicateId, duplicate) };
        }

        public AddressTakenScalarPromotionPlan Plan(
            StructuredMethod method,
            MethodInstanceModel target,
            ManagedByRefMethodEffects effects,
            bool enabled = true) => new AddressTakenScalarPromotionPlanner().Plan(
                Request(method, target, effects, enabled));

        public AddressTakenScalarPromotionRequest Request(
            StructuredMethod method,
            MethodInstanceModel target,
            ManagedByRefMethodEffects effects,
            bool enabled = true)
        {
            var targetIdentity = new ManagedMethodIdentity(target.CanonicalName);
            var calls = method.Header.Instructions
                .Where(instruction => instruction.Operation == CilOperation.Call)
                .Select(instruction =>
                {
                    var key = new ManagedCallSiteKey(Caller, instruction.Offset);
                    return new ManagedCallSite(
                        key,
                        ManagedCallOperation.Direct,
                        targetIdentity,
                        target,
                        null);
                })
                .ToImmutableDictionary(call => call.Key);
            return new(
                method,
                Caller,
                Lookups.Of(calls),
                Lookups.Of(ImmutableDictionary<string, ManagedByRefMethodEffects>.Empty
                    .Add(targetIdentity.CanonicalName, effects)),
                enabled);
        }

        private static MethodDefinitionModel Definition(
            int token,
            string name,
            bool isStatic,
            MethodSignatureModel signature) => new(
                new EntityKey(Assembly, 0x06000000 + token),
                new EntityKey(Assembly, 0x02000001),
                name,
                isStatic,
                signature,
                1);

        private static readonly AssemblyIdentity Assembly = new("promotion-tests");
        private static readonly CliTypeIdentity Container = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Container",
            false);
    }
}
