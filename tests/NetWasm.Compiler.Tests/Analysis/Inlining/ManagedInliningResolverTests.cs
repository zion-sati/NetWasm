using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.Inlining;

public sealed class ManagedInliningResolverTests
{
    [Fact]
    public void ResolvesStaticByReferenceLeaf()
    {
        var fixture = new Fixture();

        var result = fixture.Resolver().Resolve(fixture.Request());

        Assert.NotNull(result);
        Assert.Same(fixture.Target, result.Method);
        Assert.Equal(
            fixture.Body with { MethodInstance = fixture.Target },
            result.Body);
        Assert.Same(fixture.Target, result.Body.MethodInstance);
        Assert.Equal(1, fixture.SpecializerCalls);
        Assert.Equal(1, fixture.BodyValidationCalls);
        Assert.NotEmpty(result.InstructionEntryStacks);
    }

    [Fact]
    public void ResolvesAggressiveScalarLeaf()
    {
        var fixture = new Fixture(
            byReferenceParameter: false,
            aggressive: true);

        Assert.NotNull(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Theory]
    [InlineData(AggregateWrapper.Valid)]
    [InlineData(AggregateWrapper.WithConstant)]
    public void ResolvesAggregateAddressForwardingWrapper(AggregateWrapper wrapper)
    {
        var fixture = new Fixture(
            byReferenceParameter: false,
            aggregateWrapper: wrapper);

        Assert.NotNull(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Theory]
    [InlineData(AggregateWrapper.NoAddress)]
    [InlineData(AggregateWrapper.ScalarAddress)]
    [InlineData(AggregateWrapper.HasLocal)]
    [InlineData(AggregateWrapper.UnsupportedOperation)]
    [InlineData(AggregateWrapper.DifferentReturn)]
    [InlineData(AggregateWrapper.NoCall)]
    [InlineData(AggregateWrapper.BadAddressIndex)]
    [InlineData(AggregateWrapper.BadReturnIndex)]
    public void RejectsIncompleteAggregateWrapperProof(AggregateWrapper wrapper)
    {
        var fixture = new Fixture(
            byReferenceParameter: false,
            aggregateWrapper: wrapper);

        Assert.Null(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Theory]
    [InlineData(CilOperation.Call, false, false)]
    [InlineData(CilOperation.Call, true, false)]
    [InlineData(CilOperation.CallVirtual, false, false)]
    [InlineData(CilOperation.CallVirtual, true, true)]
    public void ResolvesDirectReferenceInstanceFieldAccessor(
        CilOperation operation,
        bool isVirtual,
        bool isFinal)
    {
        var fixture = new Fixture(
            callOperation: operation,
            instanceFieldAccessor: true,
            isVirtual: isVirtual,
            isFinal: isFinal);

        Assert.NotNull(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Fact]
    public void ResolvesReferenceFieldAddressAccessorWithNops()
    {
        var fixture = new Fixture(
            callOperation: CilOperation.CallVirtual,
            instanceFieldAccessor: true,
            fieldOperation: CilOperation.LoadFieldAddress,
            includeAccessorNops: true);

        Assert.NotNull(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Fact]
    public void PreservesCallsThatTriggerTypeInitialization()
    {
        var fixture = new Fixture { RequiresInitialization = true };
        Assert.Null(fixture.Resolver().Resolve(fixture.Request()));
        Assert.Equal(0, fixture.SpecializerCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PreservesCallsTheCompilerReplaces(int kind)
    {
        // Runtime intrinsics and imports have placeholder or stub bodies.
        var fixture = new Fixture(import: kind) { IsIntrinsic = kind == 0, SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit };

        Assert.Null(fixture.Resolver().Resolve(fixture.Request()));
        Assert.Equal(0, fixture.SpecializerCalls);
    }

    [Fact]
    public void DisabledPolicyDoesNotConsultMetadata()
    {
        var resolver = new ManagedInliningResolver(
            new UnusedMethodBodyReader(),
            new UnusedMethodSpecializer(),
            new UnusedCalledMethodResolver(),
            new UnusedManagedInliningBodyValidator(),
            ManagedMethodInliningPolicy.Disabled, new Fixture(), new Fixture(), 0, 0, 0);

        Assert.Null(resolver.Resolve(new(
            ValidationTestData.Instance(),
            new(0, 1, CilOperation.Call, new CilOperand.None()))));
    }

    [Theory]
    [InlineData(RejectedCandidate.Unresolved)]
    [InlineData(RejectedCandidate.NonCall)]
    [InlineData(RejectedCandidate.Instance)]
    [InlineData(RejectedCandidate.NoBody)]
    [InlineData(RejectedCandidate.NoInlining)]
    [InlineData(RejectedCandidate.OpenLocal)]
    [InlineData(RejectedCandidate.Recursive)]
    [InlineData(RejectedCandidate.NotPriority)]
    [InlineData(RejectedCandidate.WrongBody)]
    [InlineData(RejectedCandidate.ExceptionRegion)]
    [InlineData(RejectedCandidate.Empty)]
    [InlineData(RejectedCandidate.ReturnNotLast)]
    [InlineData(RejectedCandidate.LocalTypeMismatch)]
    [InlineData(RejectedCandidate.StaticCallVirtual)]
    [InlineData(RejectedCandidate.ValueTypeInstance)]
    [InlineData(RejectedCandidate.OverridableCallVirtual)]
    public void PreservesCallsWithoutCompleteLeafProof(RejectedCandidate rejection)
    {
        var fixture = new Fixture(rejection: rejection);

        Assert.Null(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Fact]
    public void InlinesClosedConstructedMethods()
    {
        var fixture = new Fixture(constructed: true);

        Assert.Same(fixture.Target, fixture.Resolver().Resolve(fixture.Request())?.Method);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void ConstructedBodiesMoveOnlyWhenClosed(int shape, bool open)
    {
        var assembly = new AssemblyIdentity("InliningTests");
        var closed = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var parameter = CliTypeIdentity.GenericParameter(false, 0);
        var owner = CliTypeIdentity.Named(assembly, "", "Owner", false);
        var callee = new MethodDefinitionModel(new(assembly, 0x06000009), new(assembly, 0x02000001), "Other", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        var field = new FieldDefinitionModel(new(assembly, 0x04000009), new(assembly, 0x02000001), "_value", CliValueKind.I4, true);
        (CilOperation Operation, CilOperand Operand) access = shape switch
        {
            0 => (CilOperation.LoadObject, new CilOperand.TypeIdentity(parameter)),
            1 => (CilOperation.Call, new CilOperand.MethodInstance(new(callee, parameter, [], callee.Signature))),
            2 => (CilOperation.Call, new CilOperand.MethodInstance(new(callee, owner, [parameter], callee.Signature))),
            3 => (CilOperation.LoadStaticField, new CilOperand.FieldInstance(new(field, parameter, closed))),
            4 => (CilOperation.LoadStaticField, new CilOperand.FieldInstance(new(field, owner, parameter))),
            5 => (CilOperation.Call, new CilOperand.MethodInstance(new(callee, owner, [closed], callee.Signature))),
            6 => (CilOperation.LoadStaticField, new CilOperand.FieldInstance(new(field, owner, closed))),
            _ => (CilOperation.LoadObject, new CilOperand.TypeIdentity(closed)),
        };
        var fixture = new Fixture(constructed: true, instructions:
        [
            new(0, 1, access.Operation, access.Operand),
            new(1, 2, CilOperation.Return, new CilOperand.None()),
        ]);

        Assert.Equal(open, fixture.Resolver().Resolve(fixture.Request()) is null);
    }

    [Theory]
    [InlineData(CilOperation.Branch)]
    [InlineData(CilOperation.BranchIfTrue)]
    [InlineData(CilOperation.BranchIfFalse)]
    [InlineData(CilOperation.BranchIfEqual)]
    [InlineData(CilOperation.BranchIfNotEqual)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned)]
    [InlineData(CilOperation.BranchIfGreaterThanUnsigned)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned)]
    [InlineData(CilOperation.BranchIfLessThanSigned)]
    [InlineData(CilOperation.BranchIfLessThanUnsigned)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualSigned)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualUnsigned)]
    [InlineData(CilOperation.Switch)]
    public void ResolvesSupportedControlFlow(CilOperation operation)
    {
        var fixture = new Fixture(operation: operation);

        Assert.NotNull(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Fact]
    public void ResolvesMultipleReturns()
    {
        var fixture = new Fixture(rejection: RejectedCandidate.MultipleReturns);

        Assert.NotNull(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Fact]
    public void InlinesBodiesThatThrow()
    {
        var fixture = new Fixture(operation: CilOperation.Throw);

        Assert.NotNull(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Theory]
    // A value type's instance methods and constructors take it by address
    // from a direct call or a construction; an object's constructor needs an
    // allocation first.
    [InlineData(CilOperation.Call, true, true)]
    [InlineData(CilOperation.NewObject, true, true)]
    [InlineData(CilOperation.NewObject, false, false)]
    public void ValueTypeInstancesArriveByAddress(CilOperation operation, bool valueType, bool resolves)
    {
        var fixture = new Fixture(instanceFieldAccessor: true, callOperation: operation, valueType: valueType);

        Assert.Equal(resolves, fixture.Resolver().Resolve(fixture.Request()) is not null);
    }

    [Theory]
    [InlineData(CilOperation.Rethrow)]
    [InlineData(CilOperation.Leave)]
    [InlineData(CilOperation.EndFinally)]
    [InlineData(CilOperation.EndFilter)]
    [InlineData(CilOperation.LocalAllocate)]
    public void RejectsExceptionalOperations(CilOperation operation)
    {
        var fixture = new Fixture(operation: operation);

        Assert.Null(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Theory]
    [InlineData(CilOperation.Branch)]
    [InlineData(CilOperation.Switch)]
    public void RejectsInvalidControlFlowTargets(CilOperation operation)
    {
        var fixture = new Fixture(operation: operation, invalidTarget: true);

        Assert.Null(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.CallVirtual)]
    [InlineData(CilOperation.CallIndirect)]
    [InlineData(CilOperation.NewObject)]
    public void AllowsNestedCallsWithoutRecursivelyInliningThem(CilOperation operation)
    {
        var fixture = new Fixture(operation: operation);

        Assert.NotNull(fixture.Resolver().Resolve(fixture.Request()));
    }

    [Theory]
    [InlineData(false, CilOperation.Call, true)]
    [InlineData(true, CilOperation.Call, true)]
    [InlineData(true, CilOperation.CallVirtual, false)]
    public void ResolvesSmallCalleesAtTheSpeedLevel(bool instance, CilOperation operation, bool resolves)
    {
        // A scalar static leaf is no priority candidate, and an instance leaf is no field accessor.
        var fixture = new Fixture(byReferenceParameter: false, callOperation: operation, instance: instance)
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
        };

        Assert.Equal(resolves, fixture.Resolver().Resolve(fixture.Request()) is not null);
        var limited = new Fixture(byReferenceParameter: false, callOperation: operation, instance: instance);
        Assert.Null(limited.Resolver().Resolve(limited.Request()));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void NestedCallsTakeOnlySmallOrAggressiveCallees(bool nested, bool aggressive, bool resolves)
    {
        // A by-reference parameter makes the leaf a priority candidate of any size.
        var fixture = new Fixture(aggressive: aggressive);
        var request = fixture.Request() with { Nested = nested };

        Assert.Equal(resolves, fixture.Resolver().Resolve(request) is not null);
        var small = new Fixture { SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit };
        Assert.NotNull(small.Resolver().Resolve(small.Request() with { Nested = true }));
    }

    [Theory]
    [InlineData(CilOperation.NewArray, false, false, false)]
    [InlineData(CilOperation.Box, false, false, false)]
    [InlineData(CilOperation.NewObject, false, false, false)]
    [InlineData(CilOperation.NewObject, true, false, true)]
    // An exception constructed for a throw is the cold path.
    [InlineData(CilOperation.NewObject, false, true, true)]
    [InlineData(CilOperation.NewArray, false, true, false)]
    public void NestedCallsLeaveAllocatingCalleesAsCalls(CilOperation operation, bool valueType, bool thrown, bool resolves)
    {
        // Constructing a value type initializes storage in place rather than allocating.
        var constructed = valueType ? CliTypeIdentity.Named(new AssemblyIdentity("InliningTests"), "", "Value", true) : null;
        var fixture = new Fixture(byReferenceParameter: false, instructions:
        [
            new(0, 1, operation, operation == CilOperation.NewObject
                ? new CilOperand.MethodInstance(new(new(new(new AssemblyIdentity("InliningTests"), 0x06000009),
                    new(new AssemblyIdentity("InliningTests"), 0x02000002), ".ctor", false, MethodSignatureModel.Create(CliValueKind.Void), 1),
                    constructed ?? CliTypeIdentity.Named(new AssemblyIdentity("InliningTests"), "", "Reference", false), [],
                    MethodSignatureModel.Create(CliValueKind.Void)))
                : new CilOperand.None()),
            new(1, 2, thrown ? CilOperation.Throw : CilOperation.Nop, new CilOperand.None()),
            new(2, 3, CilOperation.Return, new CilOperand.None()),
        ])
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
        };

        var resolver = fixture.Resolver();
        Assert.NotNull(resolver.Resolve(fixture.Request()));
        Assert.Equal(resolves, resolver.Resolve(fixture.Request() with { Nested = true }) is not null);
    }

    [Theory]
    [InlineData(false, 3, 1, true)]
    [InlineData(true, 3, 1, true)]
    [InlineData(true, 17, 1, false)]
    [InlineData(true, 3, 0, false)]
    public void AWrapperOfALargePriorityCalleeIsNotSmall(bool aggressive, int calleeInstructions, int calleeBody, bool resolves)
    {
        // The leaf calls another method. An aggressive callee is a priority
        // candidate, so a large one makes the wrapper really its size, while a
        // small one composes like any small callee.
        var fixture = new Fixture(byReferenceParameter: false, instructions:
        [
            new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new(1, 2, CilOperation.Call, new CilOperand.None()),
            new(2, 3, CilOperation.Return, new CilOperand.None()),
        ])
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            CalleeShape = (aggressive, calleeInstructions, calleeBody),
        };

        Assert.Equal(resolves, fixture.Resolver().Resolve(fixture.Request() with { Nested = true }) is not null);
    }

    [Fact]
    public void ExaminesEachCalleeOnceAndRejectsLargeBodies()
    {
        var fixture = new Fixture(byReferenceParameter: false) { SmallBodyLimit = 1 };
        var resolver = fixture.Resolver();

        Assert.Null(resolver.Resolve(fixture.Request()));
        Assert.Null(resolver.Resolve(fixture.Request()));
        Assert.Equal(1, fixture.SpecializerCalls);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(11, false)]
    public void ResolvesAVirtualCallOnlyWhenTheCalleeDereferencesItsReceiverFirst(int shape, bool resolves)
    {
        static CilInstruction Load(int offset, int argument) => new(offset, offset + 1, CilOperation.LoadArgument, new CilOperand.Index(argument));
        var field = new CilOperand.Entity(new(new AssemblyIdentity("InliningTests"), 0x04000001));
        ImmutableArray<CilInstruction> body = shape switch
        {
            0 => [Load(0, 0), new(1, 2, CilOperation.LoadField, field), new(2, 3, CilOperation.Pop, new CilOperand.None()), new(3, 4, CilOperation.Return, new CilOperand.None())],
            1 => [Load(0, 0), Load(1, 1), new(2, 3, CilOperation.StoreField, field), new(3, 4, CilOperation.Return, new CilOperand.None())],
            2 => [Load(0, 0), new(1, 2, CilOperation.LoadNull, new CilOperand.None()), new(2, 3, CilOperation.StoreField, field), new(3, 4, CilOperation.Return, new CilOperand.None())],
            3 => [Load(0, 1), Load(1, 0), new(2, 3, CilOperation.LoadField, field), new(3, 4, CilOperation.Return, new CilOperand.None())],
            4 => [Load(0, 0), Load(1, 1), new(2, 3, CilOperation.Add, new CilOperand.None()), new(3, 4, CilOperation.Return, new CilOperand.None())],
            5 => [Load(0, 0), Load(1, 1), new(2, 3, CilOperation.Pop, new CilOperand.None()), new(3, 4, CilOperation.Pop, new CilOperand.None()), new(4, 5, CilOperation.Return, new CilOperand.None())],
            // this._count += value
            6 => [Load(0, 0), Load(1, 0), new(2, 3, CilOperation.LoadField, field), Load(3, 1), new(4, 5, CilOperation.Add, new CilOperand.None()),
                new(5, 6, CilOperation.StoreField, field), new(6, 7, CilOperation.Return, new CilOperand.None())],
            7 => [Load(0, 0), Load(1, 1), new(2, 3, CilOperation.LoadField, field), new(3, 4, CilOperation.Return, new CilOperand.None())],
            8 => [Load(0, 1), Load(1, 0), new(2, 3, CilOperation.StoreField, field), new(3, 4, CilOperation.Return, new CilOperand.None())],
            // The dereferenced object is a constant, or an argument load without an index.
            10 => [new(0, 1, CilOperation.LoadNull, new CilOperand.None()), new(1, 2, CilOperation.LoadField, field), new(2, 3, CilOperation.Return, new CilOperand.None())],
            11 => [new(0, 1, CilOperation.LoadArgument, new CilOperand.None()), new(1, 2, CilOperation.LoadField, field), new(2, 3, CilOperation.Return, new CilOperand.None())],
            _ => [new(0, 1, CilOperation.StoreField, field), new(1, 2, CilOperation.Return, new CilOperand.None())],
        };
        var fixture = new Fixture(byReferenceParameter: false, callOperation: CilOperation.CallVirtual, instance: true, instructions: body)
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
        };

        Assert.Equal(resolves, fixture.Resolver().Resolve(fixture.Request()) is not null);
    }

    [Fact]
    public void RejectsInvalidConstructionAndRequest()
    {
        var fixture = new Fixture();

        Assert.Throws<ArgumentNullException>(() => new ManagedInliningResolver(
            null!, fixture, fixture, fixture, ManagedMethodInliningPolicy.Speed, fixture, fixture, 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => new ManagedInliningResolver(
            fixture, null!, fixture, fixture, ManagedMethodInliningPolicy.Speed, fixture, fixture, 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => new ManagedInliningResolver(
            fixture, fixture, null!, fixture, ManagedMethodInliningPolicy.Speed, fixture, fixture, 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => new ManagedInliningResolver(
            fixture, fixture, fixture, null!, ManagedMethodInliningPolicy.Speed, fixture, fixture, 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => new ManagedInliningResolver(
            fixture, fixture, fixture, fixture, ManagedMethodInliningPolicy.Speed, null!, fixture, 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => new ManagedInliningResolver(
            fixture, fixture, fixture, fixture, ManagedMethodInliningPolicy.Speed, fixture, null!, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedInliningResolver(
            fixture, fixture, fixture, fixture, (ManagedMethodInliningPolicy)255, fixture, fixture, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedInliningResolver(
            fixture, fixture, fixture, fixture, ManagedMethodInliningPolicy.Speed, fixture, fixture, -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedInliningResolver(
            fixture, fixture, fixture, fixture, ManagedMethodInliningPolicy.Speed, fixture, fixture, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedInliningResolver(
            fixture, fixture, fixture, fixture, ManagedMethodInliningPolicy.Speed, fixture, fixture, 0, 0, -1));
        Assert.Throws<ArgumentNullException>(() => fixture.Resolver().Resolve(null!));
    }

    [Theory]
    [InlineData(LeafShape.Straight, 17, true)]
    [InlineData(LeafShape.Straight, 32, true)]
    [InlineData(LeafShape.Straight, 33, false)]
    // A branch inside the caller's loop can hide that loop from bounds-check elimination.
    [InlineData(LeafShape.ForwardBranch, 17, false)]
    [InlineData(LeafShape.BackwardBranch, 17, false)]
    [InlineData(LeafShape.Switch, 17, false)]
    [InlineData(LeafShape.Call, 17, false)]
    [InlineData(LeafShape.ArrayAllocation, 17, false)]
    [InlineData(LeafShape.ObjectAllocation, 17, false)]
    // Without a branch the throw is unconditional: a throw helper stays a call.
    [InlineData(LeafShape.ThrownException, 17, false)]
    public void InlinesLeafCalleesLargerThanTheSmallLimit(LeafShape shape, int instructions, bool resolves)
    {
        // A scalar static callee is no priority candidate, so only the leaf tier can take it.
        var leaf = new Fixture(byReferenceParameter: false, instructions: LeafBody(shape, instructions))
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            LeafBodyLimit = ManagedInliningResolver.LeafBodyInstructionLimit,
        };

        var inLoop = leaf.Request() with { InLoop = true };
        Assert.Equal(resolves, leaf.Resolver().Resolve(inLoop) is not null);
        Assert.Equal(resolves, leaf.Resolver().Resolve(inLoop with { Nested = true }) is not null);
        // Called once, the leaf only adds register pressure to the rest of its caller.
        Assert.Null(leaf.Resolver().Resolve(leaf.Request()));
        var withoutLeafTier = new Fixture(byReferenceParameter: false, instructions: LeafBody(shape, instructions))
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
        };
        Assert.Null(withoutLeafTier.Resolver().Resolve(withoutLeafTier.Request() with { InLoop = true }));
    }

    // An operator returning new Complex(...) constructs its result in place;
    // with a constructor the next round inlines, it is still a leaf.
    [Theory]
    [InlineData(ManagedInliningResolver.SmallBodyInstructionLimit, true)]
    [InlineData(ManagedInliningResolver.SmallBodyInstructionLimit + 1, false)]
    public void ALeafMayConstructAValueWhoseConstructorTheNextRoundInlines(int constructorInstructions, bool resolves)
    {
        var leaf = new Fixture(byReferenceParameter: false, instructions: LeafBody(LeafShape.ValueConstruction, 17))
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            LeafBodyLimit = ManagedInliningResolver.LeafBodyInstructionLimit,
            CalleeShape = (false, constructorInstructions, 1),
        };

        Assert.Equal(resolves, leaf.Resolver().Resolve(leaf.Request() with { InLoop = true }) is not null);
    }

    [Theory]
    // Inside a loop, a callee larger than a leaf may branch forward.
    [InlineData(LeafShape.ForwardBranch, 33, true)]
    [InlineData(LeafShape.ForwardBranch, ManagedInliningResolver.LoopBodyInstructionLimit, true)]
    [InlineData(LeafShape.ForwardBranch, ManagedInliningResolver.LoopBodyInstructionLimit + 1, false)]
    [InlineData(LeafShape.Straight, 40, true)]
    [InlineData(LeafShape.Switch, 40, true)]
    // A branching callee no larger than a leaf is left to Binaryen's inliner.
    [InlineData(LeafShape.ForwardBranch, ManagedInliningResolver.LeafBodyInstructionLimit, false)]
    [InlineData(LeafShape.ManyBranches, 40, false)]
    [InlineData(LeafShape.BackwardBranch, 40, false)]
    [InlineData(LeafShape.ArrayAllocation, 40, false)]
    [InlineData(LeafShape.ObjectAllocation, 40, false)]
    [InlineData(LeafShape.ThrownException, 40, false)]
    public void InlinesBranchingCalleesWithinTheLoopBodySize(LeafShape shape, int instructions, bool resolves)
    {
        var callee = new Fixture(byReferenceParameter: false, instructions: LeafBody(shape, instructions))
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            LeafBodyLimit = ManagedInliningResolver.LeafBodyInstructionLimit,
            LoopBodyLimit = ManagedInliningResolver.LoopBodyInstructionLimit,
        };

        var inLoop = callee.Request() with { InLoop = true };
        Assert.Equal(resolves, callee.Resolver().Resolve(inLoop) is not null);
        // Called once, the callee would only add code to the rest of its caller.
        Assert.Null(callee.Resolver().Resolve(callee.Request()));
        var withoutLoopBodyTier = new Fixture(byReferenceParameter: false, instructions: LeafBody(shape, instructions))
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            LeafBodyLimit = ManagedInliningResolver.LeafBodyInstructionLimit,
        };
        Assert.Null(withoutLoopBodyTier.Resolver().Resolve(withoutLoopBodyTier.Request() with { InLoop = true }));
    }

    [Theory]
    // A call the next round inlines leaves the loop without a call of its own.
    [InlineData(true, 40, true)]
    [InlineData(false, ManagedInliningResolver.SmallBodyInstructionLimit, true)]
    [InlineData(false, ManagedInliningResolver.SmallBodyInstructionLimit + 1, false)]
    public void ALoopBodyCallsOnlyWhatTheNextRoundInlines(bool aggressive, int calleeInstructions, bool resolves)
    {
        var caller = new Fixture(byReferenceParameter: false, instructions: LeafBody(LeafShape.Call, 40))
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            LeafBodyLimit = ManagedInliningResolver.LeafBodyInstructionLimit,
            LoopBodyLimit = ManagedInliningResolver.LoopBodyInstructionLimit,
            CalleeShape = (aggressive, calleeInstructions, 1),
        };

        Assert.Equal(resolves, caller.Resolver().Resolve(caller.Request() with { InLoop = true }) is not null);
    }

    [Theory]
    // Inside a loop the loops policy takes what the speed policy takes there: a small
    // body, a leaf or a loop body, but not the unbounded priority shapes. Outside a loop
    // it takes a callee marked AggressiveInlining, as the aggressive policy does, and a
    // field accessor, which inlining only shrinks.
    [InlineData(CalleeShape.Small, false, true, true)]
    [InlineData(CalleeShape.Small, false, false, false)]
    [InlineData(CalleeShape.Leaf, false, true, true)]
    [InlineData(CalleeShape.Leaf, false, false, false)]
    [InlineData(CalleeShape.LoopBody, false, true, true)]
    [InlineData(CalleeShape.LoopBody, false, false, false)]
    [InlineData(CalleeShape.ByReferencePriority, false, true, false)]
    [InlineData(CalleeShape.ByReferencePriority, false, false, false)]
    [InlineData(CalleeShape.Small, true, true, true)]
    [InlineData(CalleeShape.Small, true, false, true)]
    [InlineData(CalleeShape.ByReferencePriority, true, false, true)]
    [InlineData(CalleeShape.FieldAccessor, false, true, true)]
    [InlineData(CalleeShape.FieldAccessor, false, false, true)]
    public void TheLoopsPolicyInlinesInsideLoopsAndWhatTheCalleeAsksFor(CalleeShape shape, bool aggressive, bool inLoop, bool resolves)
    {
        var callee = new Fixture(byReferenceParameter: shape == CalleeShape.ByReferencePriority, aggressive: aggressive,
            callOperation: shape == CalleeShape.FieldAccessor ? CilOperation.CallVirtual : CilOperation.Call,
            instanceFieldAccessor: shape == CalleeShape.FieldAccessor,
            instructions: shape switch
            {
                CalleeShape.FieldAccessor => null,
                CalleeShape.Leaf => LeafBody(LeafShape.Straight, 24),
                CalleeShape.LoopBody => LeafBody(LeafShape.ForwardBranch, 40),
                // Larger than every size tier: only its byref parameter makes it a candidate.
                CalleeShape.ByReferencePriority => LeafBody(LeafShape.Straight, 60),
                _ => LeafBody(LeafShape.Straight, 8),
            })
        {
            Policy = ManagedMethodInliningPolicy.Loops,
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            LeafBodyLimit = ManagedInliningResolver.LeafBodyInstructionLimit,
            LoopBodyLimit = ManagedInliningResolver.LoopBodyInstructionLimit,
        };

        var request = callee.Request() with { InLoop = inLoop };
        Assert.Equal(resolves, callee.Resolver().Resolve(request) is not null);
        Assert.Equal(resolves, callee.Resolver().Resolve(request with { Nested = true }) is not null);
    }

    public enum CalleeShape { Small, Leaf, LoopBody, ByReferencePriority, FieldAccessor }

    // An inlined callvirt target must fault on a null receiver before any other
    // effect, as the callvirt would. Reading a property of the receiver first
    // does: a callvirt checks it, and the accessor a call reaches loads from it.
    [Theory]
    [InlineData(CilOperation.Call, true, true)]
    [InlineData(CilOperation.CallVirtual, true, true)]
    [InlineData(CilOperation.Call, false, false)]
    [InlineData(CilOperation.CallVirtual, false, false)]
    public void ACallvirtTargetMayReadAReceiverPropertyFirst(CilOperation read, bool onReceiver, bool resolves)
    {
        var callee = new Fixture(byReferenceParameter: false, callOperation: CilOperation.CallVirtual, instance: true,
            instructions:
            [
                onReceiver
                    ? new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0))
                    : new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
                new(1, 2, read, new CilOperand.Entity(Fixture.AccessorKey)),
                new(2, 3, CilOperation.Return, new CilOperand.None()),
            ])
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
        };

        Assert.Equal(resolves, callee.Resolver().Resolve(callee.Request()) is not null);
    }

    [Theory]
    // Only a callee marked AggressiveInlining asks to be inlined; a byref
    // parameter, a small body or a loop body asks for nothing.
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, false)]
    public void TheAggressivePolicyInlinesOnlyWhatTheCalleeAsksFor(bool aggressive, bool byReference, bool loopBody, bool resolves)
    {
        var callee = new Fixture(byReferenceParameter: byReference, aggressive: aggressive,
            instructions: loopBody ? LeafBody(LeafShape.ForwardBranch, 40) : LeafBody(LeafShape.Straight, 8))
        {
            Policy = ManagedMethodInliningPolicy.Aggressive,
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            LeafBodyLimit = ManagedInliningResolver.LeafBodyInstructionLimit,
            LoopBodyLimit = ManagedInliningResolver.LoopBodyInstructionLimit,
        };

        var inLoop = callee.Request() with { InLoop = true };
        Assert.Equal(resolves, callee.Resolver().Resolve(inLoop) is not null);
        Assert.Equal(resolves, callee.Resolver().Resolve(inLoop with { Nested = true }) is not null);
        // The speed policy takes every one of these.
        Assert.NotNull(new Fixture(byReferenceParameter: byReference, aggressive: aggressive,
            instructions: loopBody ? LeafBody(LeafShape.ForwardBranch, 40) : LeafBody(LeafShape.Straight, 8))
        {
            SmallBodyLimit = ManagedInliningResolver.SmallBodyInstructionLimit,
            LeafBodyLimit = ManagedInliningResolver.LeafBodyInstructionLimit,
            LoopBodyLimit = ManagedInliningResolver.LoopBodyInstructionLimit,
        }.Resolver().Resolve(inLoop));
    }

    public enum LeafShape
    {
        Straight,
        ForwardBranch,
        BackwardBranch,
        Switch,
        Call,
        ArrayAllocation,
        ObjectAllocation,
        ValueConstruction,
        ThrownException,
        ManyBranches,
    }

    // Counts every instruction, including the final return.
    private static ImmutableArray<CilInstruction> LeafBody(LeafShape shape, int count)
    {
        var assembly = new AssemblyIdentity("InliningTests");
        var constructor = new CilOperand.MethodInstance(new(
            new(new(assembly, 0x06000009), new(assembly, 0x02000002), ".ctor", false,
                MethodSignatureModel.Create(CliValueKind.Void), 1),
            CliTypeIdentity.Named(assembly, "", "Reference", false), [],
            MethodSignatureModel.Create(CliValueKind.Void)));
        CilInstruction[] head = shape switch
        {
            LeafShape.ForwardBranch => [new(0, 1, CilOperation.Branch, new CilOperand.BranchTarget(count - 1))],
            LeafShape.BackwardBranch => [new(0, 1, CilOperation.Branch, new CilOperand.BranchTarget(0))],
            LeafShape.Switch => [new(0, 1, CilOperation.Switch, new CilOperand.SwitchTargets([count - 1]))],
            LeafShape.ManyBranches => [.. Enumerable.Range(0, ManagedInliningResolver.LoopBodyBranchLimit + 1).Select(index =>
                new CilInstruction(index, index + 1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(count - 1)))],
            LeafShape.Call => [new(0, 1, CilOperation.Call, new CilOperand.None())],
            LeafShape.ArrayAllocation => [new(0, 1, CilOperation.NewArray,
                new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("i4", CliValueKind.I4)))],
            LeafShape.ObjectAllocation => [new(0, 1, CilOperation.NewObject, constructor)],
            LeafShape.ValueConstruction => [new(0, 1, CilOperation.NewObject, new CilOperand.MethodInstance(
                ((CilOperand.MethodInstance)constructor).Value with
                {
                    DeclaringType = CliTypeIdentity.Named(assembly, "", "Value", true),
                }))],
            LeafShape.ThrownException =>
            [
                new(0, 1, CilOperation.NewObject, constructor),
                new(1, 2, CilOperation.Throw, new CilOperand.None()),
            ],
            _ => [],
        };
        return
        [
            .. head,
            .. Enumerable.Range(head.Length, count - 1 - head.Length).Select(static index =>
                new CilInstruction(index, index + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(index))),
            new(count - 1, count, CilOperation.Return, new CilOperand.None()),
        ];
    }

    public enum RejectedCandidate
    {
        Unresolved,
        NonCall,
        Instance,
        NoBody,
        NoInlining,
        OpenLocal,
        Recursive,
        NotPriority,
        WrongBody,
        ExceptionRegion,
        Empty,
        ReturnNotLast,
        MultipleReturns,
        LocalTypeMismatch,
        StaticCallVirtual,
        ValueTypeInstance,
        OverridableCallVirtual,
    }

    public enum AggregateWrapper
    {
        Valid,
        WithConstant,
        NoAddress,
        ScalarAddress,
        HasLocal,
        UnsupportedOperation,
        DifferentReturn,
        NoCall,
        BadAddressIndex,
        BadReturnIndex,
    }

    private sealed class Fixture :
        IMethodBodyReader,
        IMethodSpecializer,
        ICalledMethodResolver,
        IManagedInliningBodyValidator,
        IMethodInitializationTriggerClassifier,
        IRuntimeIntrinsicRegistry
    {
        private static readonly AssemblyIdentity Assembly = new("InliningTests");
        private static readonly CliTypeIdentity Int32 =
            CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        private static readonly CliTypeIdentity Owner =
            CliTypeIdentity.Named(Assembly, "", "Owner", false);
        private static readonly CliTypeIdentity Aggregate =
            CliTypeIdentity.Named(Assembly, "", "Aggregate", true);
        private static readonly CliTypeIdentity OtherAggregate =
            CliTypeIdentity.Named(Assembly, "", "OtherAggregate", true);
        private readonly RejectedCandidate? _rejection;
        private readonly CilOperation? _operation;
        private readonly CilOperation _callOperation;
        private readonly bool _invalidTarget;
        private readonly ImmutableArray<CilInstruction>? _instructions;

        public Fixture(
            bool byReferenceParameter = true,
            bool aggressive = false,
            RejectedCandidate? rejection = null,
            CilOperation? operation = null,
            CilOperation callOperation = CilOperation.Call,
            bool instanceFieldAccessor = false,
            bool isVirtual = false,
            bool isFinal = false,
            CilOperation fieldOperation = CilOperation.LoadField,
            bool includeAccessorNops = false,
            AggregateWrapper? aggregateWrapper = null,
            bool invalidTarget = false,
            bool instance = false,
            ImmutableArray<CilInstruction>? instructions = null,
            int import = 0,
            bool constructed = false,
            bool valueType = false)
        {
            valueType |= rejection == RejectedCandidate.ValueTypeInstance;
            constructed |= rejection == RejectedCandidate.OpenLocal;
            _rejection = rejection;
            _instructions = instructions;
            _operation = operation;
            _callOperation = rejection is
                RejectedCandidate.StaticCallVirtual or
                RejectedCandidate.ValueTypeInstance or
                RejectedCandidate.OverridableCallVirtual
                ? CilOperation.CallVirtual
                : callOperation;
            _invalidTarget = invalidTarget;
            var isInstance = instance || instanceFieldAccessor || rejection is
                RejectedCandidate.Instance or
                RejectedCandidate.ValueTypeInstance or
                RejectedCandidate.OverridableCallVirtual;
            var parameter = aggregateWrapper is null
                ? byReferenceParameter && rejection != RejectedCandidate.NotPriority
                    ? CliTypeIdentity.ManagedByReference(Int32)
                    : Int32
                : Aggregate;
            var returnType = aggregateWrapper == AggregateWrapper.DifferentReturn
                ? OtherAggregate
                : aggregateWrapper is null
                    ? Int32
                    : Aggregate;
            ImmutableArray<CliTypeIdentity> parameters =
                aggregateWrapper == AggregateWrapper.ScalarAddress
                ? [Aggregate, Int32]
                : aggregateWrapper is null
                    ? [parameter]
                    : [Aggregate, Aggregate];
            CliTypeIdentity[] signatureParameters = isInstance
                ? []
                : [.. parameters];
            var definition = new MethodDefinitionModel(
                new(Assembly, 0x06000002),
                new(Assembly, 0x02000001),
                "Leaf",
                !isInstance,
                MethodSignatureModel.Create(
                    returnType,
                    signatureParameters),
                rejection == RejectedCandidate.NoBody ? 0 : 1)
            {
                IsAggressiveInlining = aggressive,
                IsNoInlining = rejection == RejectedCandidate.NoInlining,
                GenericArity = constructed ? 1 : 0,
                IsVirtual = isVirtual || rejection == RejectedCandidate.OverridableCallVirtual,
                IsFinal = isFinal,
                JSImport = import == 1 ? new("leaf", "fixture") : null,
                WitImport = import == 2 ? new("fixture", "leaf") : null,
                NativeImport = import == 3 ? new("fixture", "leaf", 0, false, false, false, false) : null,
            };
            Target = new(
                definition,
                valueType ? Int32 : Owner,
                constructed ? [Int32] : [],
                definition.Signature);
            Caller = rejection == RejectedCandidate.Recursive
                ? Target
                : CreateCaller();
            Body = CreateBody(instanceFieldAccessor || rejection is
                    RejectedCandidate.ValueTypeInstance or
                    RejectedCandidate.OverridableCallVirtual,
                fieldOperation,
                includeAccessorNops,
                aggregateWrapper);
        }

        public MethodInstanceModel Caller { get; }
        public MethodInstanceModel Target { get; }
        public CilMethodBody Body { get; }
        public int SpecializerCalls { get; private set; }
        public bool RequiresInitialization { get; init; }
        // The size tier is off by default so each proof tier is tested on its own.
        public int SmallBodyLimit { get; init; }
        public int LeafBodyLimit { get; init; }

        public int LoopBodyLimit { get; init; }

        public ManagedMethodInliningPolicy Policy { get; init; } = ManagedMethodInliningPolicy.Speed;
        public bool Classify(MethodDefinitionModel method) => RequiresInitialization;
        public bool IsIntrinsic { get; init; }

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = default;
            return IsIntrinsic;
        }
        public int BodyValidationCalls { get; private set; }

        public ManagedInliningResolver Resolver() => new(
            this,
            this,
            this,
            this,
            Policy, this, this, SmallBodyLimit, LeafBodyLimit, LoopBodyLimit);

        public ManagedInliningRequest Request() => new(
            Caller,
            new(
                0,
                1,
                _rejection == RejectedCandidate.NonCall
                    ? CilOperation.LoadInt32
                    : _callOperation,
                new CilOperand.MethodInstance(Target)));

        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => Body;

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) =>
            method.Definition.Key == AccessorKey
            ? Body with
            {
                Method = method.Definition,
                Instructions =
                [
                    new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    new(1, 2, CilOperation.LoadField, new CilOperand.Entity(new(Assembly, 0x04000001))),
                    new(2, 3, CilOperation.Return, new CilOperand.None()),
                ],
            }
            : method.Definition.Key == CalleeKey && CalleeShape is { } shape
                ? Body with
                {
                    Method = method.Definition,
                    Instructions =
                    [
                        .. Enumerable.Range(0, shape.Instructions - 1).Select(static index =>
                            new CilInstruction(index, index + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(index))),
                        new(shape.Instructions - 1, shape.Instructions, CilOperation.Return, new CilOperand.None()),
                    ],
                }
                : Body;

        // The method that a call without an operand in the leaf's body reaches.
        public (bool Aggressive, int Instructions, int RelativeVirtualAddress)? CalleeShape { get; init; }

        private static readonly EntityKey CalleeKey = new(Assembly, 0x06000009);

        // An instance field accessor that a call with its key as the operand reaches.
        public static readonly EntityKey AccessorKey = new(Assembly, 0x0600000a);

        private MethodInstanceModel Accessor => Target with
        {
            Definition = new MethodDefinitionModel(AccessorKey, Target.Definition.DeclaringType, "get_Value", false,
                MethodSignatureModel.Create(Int32), 1),
            Signature = MethodSignatureModel.Create(Int32),
        };

        private MethodInstanceModel Callee(bool aggressive, int relativeVirtualAddress) => Target with
        {
            Definition = new MethodDefinitionModel(CalleeKey, Target.Definition.DeclaringType, "Callee", true,
                Target.Definition.Signature, relativeVirtualAddress)
            {
                IsAggressiveInlining = aggressive,
            },
        };

        public CilMethodBody Rewrite(CilMethodBody body)
        {
            SpecializerCalls++;
            return body;
        }

        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            _rejection == RejectedCandidate.Unresolved ? null
            : instruction.Operand is CilOperand.Entity { Key: var key } && key == AccessorKey ? Accessor
            : instruction is { Operation: CilOperation.NewObject, Operand: CilOperand.MethodInstance constructor } ? constructor.Value
            : instruction.Operand is CilOperand.None && CalleeShape is { } shape ? Callee(shape.Aggressive, shape.RelativeVirtualAddress)
            : Target;

        public ImmutableDictionary<int, ImmutableArray<CliValueKind>> Validate(
            CilMethodBody body)
        {
            BodyValidationCalls++;
            return body.Instructions
                .Where(static instruction =>
                    instruction.Operation == CilOperation.Return)
                .ToImmutableDictionary(
                    static instruction => instruction.Offset,
                    _ => Target.Signature.ReturnType == CliValueKind.Void
                        ? ImmutableArray<CliValueKind>.Empty
                        : [Target.Signature.ReturnType]);
        }

        private CilMethodBody CreateBody(
            bool instanceFieldAccessor,
            CilOperation fieldOperation,
            bool includeAccessorNops,
            AggregateWrapper? aggregateWrapper)
        {
            var method = _rejection == RejectedCandidate.WrongBody
                ? new MethodDefinitionModel(
                    new(Assembly, 0x06000003),
                    Target.Definition.DeclaringType,
                    "Other",
                    true,
                    Target.Signature,
                    1)
                : Target.Definition;
            ImmutableArray<CilInstruction> instructions = _rejection switch
            {
                RejectedCandidate.Empty => [],
                RejectedCandidate.ReturnNotLast =>
                [
                    new(0, 1, CilOperation.Return, new CilOperand.None()),
                    new(1, 2, CilOperation.Nop, new CilOperand.None()),
                ],
                RejectedCandidate.MultipleReturns =>
                [
                    new(0, 1, CilOperation.Return, new CilOperand.None()),
                    new(1, 2, CilOperation.Return, new CilOperand.None()),
                ],
                _ when _instructions is { } custom => custom,
                _ when _operation is { } operation =>
                [
                    new(0, 1, operation, Operand(operation, _invalidTarget)),
                    new(1, 2, CilOperation.Return, new CilOperand.None()),
                ],
                _ when instanceFieldAccessor && includeAccessorNops =>
                [
                    new(0, 1, CilOperation.Nop, new CilOperand.None()),
                    new(1, 2, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    new(2, 3, CilOperation.Nop, new CilOperand.None()),
                    new(
                        3,
                        4,
                        fieldOperation,
                        new CilOperand.Entity(new(Assembly, 0x04000001))),
                    new(4, 5, CilOperation.Nop, new CilOperand.None()),
                    new(5, 6, CilOperation.Return, new CilOperand.None()),
                ],
                _ when instanceFieldAccessor =>
                [
                    new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    new(
                        1,
                        2,
                        fieldOperation,
                        new CilOperand.Entity(new(Assembly, 0x04000001))),
                    new(2, 3, CilOperation.Return, new CilOperand.None()),
                ],
                _ when aggregateWrapper is { } wrapper => AggregateInstructions(wrapper),
                _ =>
                [
                    new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    new(1, 2, CilOperation.Return, new CilOperand.None()),
                ],
            };
            var body = new CilMethodBody(
                method,
                1,
                _rejection is RejectedCandidate.LocalTypeMismatch or RejectedCandidate.OpenLocal ||
                    aggregateWrapper == AggregateWrapper.HasLocal
                    ? [CliValueKind.I4]
                    : [],
                instructions)
            {
                LocalSignatureTypes = aggregateWrapper == AggregateWrapper.HasLocal
                    ? [Int32]
                    : _rejection == RejectedCandidate.OpenLocal
                        ? [CliTypeIdentity.GenericParameter(true, 0)]
                        : [],
                ExceptionRegions = _rejection == RejectedCandidate.ExceptionRegion
                    ? [new(CilExceptionRegionKind.Catch, 0, 1, 1, 1, null, null)]
                    : [],
            };
            return body;
        }

        private static ImmutableArray<CilInstruction> AggregateInstructions(
            AggregateWrapper wrapper) => wrapper switch
            {
                AggregateWrapper.Valid or AggregateWrapper.HasLocal =>
                [
                    new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.LoadArgumentAddress, new CilOperand.Index(1)),
                new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(CreateCaller())),
                new(3, 4, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(4, 5, CilOperation.Return, new CilOperand.None()),
            ],
                AggregateWrapper.WithConstant =>
                [
                    new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(CreateCaller())),
                new(3, 4, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(4, 5, CilOperation.Return, new CilOperand.None()),
            ],
                AggregateWrapper.NoAddress =>
                [
                    new(0, 1, CilOperation.Call, new CilOperand.MethodInstance(CreateCaller())),
                new(1, 2, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(2, 3, CilOperation.Return, new CilOperand.None()),
            ],
                AggregateWrapper.ScalarAddress =>
                [
                    new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(1)),
                new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(CreateCaller())),
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ],
                AggregateWrapper.UnsupportedOperation =>
                [
                    new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.Pop, new CilOperand.None()),
                new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(CreateCaller())),
                new(3, 4, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(4, 5, CilOperation.Return, new CilOperand.None()),
            ],
                AggregateWrapper.DifferentReturn =>
                [
                    new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(CreateCaller())),
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ],
                AggregateWrapper.NoCall =>
                [
                    new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ],
                AggregateWrapper.BadAddressIndex =>
                [
                    new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(2)),
                new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(CreateCaller())),
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ],
                AggregateWrapper.BadReturnIndex =>
                [
                    new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(CreateCaller())),
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(2)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ],
                _ => throw new ArgumentOutOfRangeException(nameof(wrapper)),
            };

        private static MethodInstanceModel CreateCaller()
        {
            var definition = new MethodDefinitionModel(
                new(Assembly, 0x06000001),
                new(Assembly, 0x02000001),
                "Caller",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            return new(definition, Owner, [], definition.Signature);
        }

        private static CilOperand Operand(
            CilOperation operation,
            bool invalidTarget) => operation switch
            {
                CilOperation.Branch or
                CilOperation.BranchIfTrue or
                CilOperation.BranchIfFalse or
                CilOperation.BranchIfEqual or
                CilOperation.BranchIfNotEqual or
                CilOperation.BranchIfGreaterThanSigned or
                CilOperation.BranchIfGreaterThanUnsigned or
                CilOperation.BranchIfGreaterThanOrEqualSigned or
                CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                CilOperation.BranchIfLessThanSigned or
                CilOperation.BranchIfLessThanUnsigned or
                CilOperation.BranchIfLessThanOrEqualSigned or
                CilOperation.BranchIfLessThanOrEqualUnsigned or
                CilOperation.Leave => new CilOperand.BranchTarget(invalidTarget ? 99 : 1),
                CilOperation.Switch => new CilOperand.SwitchTargets([invalidTarget ? 99 : 1]),
                CilOperation.Call or
                CilOperation.CallVirtual or
                CilOperation.NewObject => new CilOperand.MethodInstance(CreateCaller()),
                _ => new CilOperand.None(),
            };
    }

    private sealed class UnusedManagedInliningBodyValidator :
        IManagedInliningBodyValidator
    {
        public ImmutableDictionary<int, ImmutableArray<CliValueKind>> Validate(
            CilMethodBody body) => throw new InvalidOperationException();
    }

    private sealed class UnusedMethodBodyReader : IMethodBodyReader
    {
        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) =>
            throw new InvalidOperationException();

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedMethodSpecializer : IMethodSpecializer
    {
        public CilMethodBody Rewrite(CilMethodBody body) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            throw new InvalidOperationException();
    }
}
