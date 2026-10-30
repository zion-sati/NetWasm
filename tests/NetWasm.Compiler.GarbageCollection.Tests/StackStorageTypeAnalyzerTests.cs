using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.GarbageCollection.Tests;

public sealed class StackStorageTypeAnalyzerTests
{
    private static readonly AssemblyIdentity Assembly = new("StackStorage");
    private static readonly EntityKey OwnerKey = new(Assembly, 0x02000001);
    private static readonly EntityKey PairKey = new(Assembly, 0x02000002);
    private static readonly EntityKey EntryKey = new(Assembly, 0x06000001);
    private static readonly EntityKey CollectKey = new(Assembly, 0x06000002);
    private static readonly EntityKey ProducerKey = new(Assembly, 0x06000003);
    private static readonly EntityKey FieldKey = new(Assembly, 0x04000001);
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "Tests", "Owner", false);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Tests", "Pair", true);
    private static readonly CliTypeIdentity Reference = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);

    [Theory]
    [InlineData(4, 0)]
    [InlineData(8, 0)]
    [InlineData(4, 1)]
    [InlineData(8, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    [InlineData(4, 3)]
    [InlineData(8, 3)]
    [InlineData(4, 4)]
    [InlineData(8, 4)]
    public void RootMapsDemandOnlyCompleteBackingsNotAlreadyCoveredByTheCurrentPointee(int pointerSize, int shape)
    {
        var repository = new Repository(pointerSize);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        repository.Methods[ProducerKey] = Method(ProducerKey, CliTypeIdentity.ManagedByReference(Pair));
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var instructions = new List<CilInstruction> { I(0, CilOperation.Call, new CilOperand.Entity(ProducerKey)) };
        if (shape != 0) instructions.Add(I(instructions.Count, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)));
        if (shape == 2)
        {
            instructions.Add(I(instructions.Count, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)));
            instructions.Add(I(instructions.Count, CilOperation.Add));
        }
        if (shape == 3) instructions.Add(I(instructions.Count, CilOperation.StoreLocal, new CilOperand.Index(0)));
        if (shape == 4) repository.Methods[CollectKey] = Method(CollectKey, Void, CliTypeIdentity.ManagedByReference(scalar));
        var offset = instructions.Count;
        instructions.Add(I(offset, CilOperation.Call, new CilOperand.Entity(CollectKey)));
        if (shape == 3) instructions.Add(I(instructions.Count, CilOperation.LoadLocal, new CilOperand.Index(0)));
        if (shape != 4) instructions.Add(I(instructions.Count, CilOperation.Pop));
        instructions.Add(I(instructions.Count, CilOperation.Return));
        var method = Body(repository, Method(EntryKey, Void),
            shape == 3 ? [CliTypeIdentity.ManagedByReference(scalar)] : [], [.. instructions]);

        var map = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>()));
        var roots = map.Safepoints[offset].Roots;
        if (shape == 0)
        {
            Assert.Empty(map.AnchorPlan.Anchors);
            Assert.Contains(new RootSource(RootSourceKind.EvaluationStack, 0, pointerSize), roots);
            return;
        }
        var location = new StackStorageLocation(shape == 3 ? StackStorageLocationKind.Local : StackStorageLocationKind.EvaluationStack, 0);
        var slot = map.AnchorPlan.Anchors.IndexOf(new(location, Pair));
        Assert.True(slot >= 0);
        Assert.Contains(new RootSource(RootSourceKind.StackStorageAnchor, slot), roots);
        Assert.Contains(new RootSource(RootSourceKind.StackStorageAnchor, slot, pointerSize), roots);
        Assert.Contains(map.AnchorPlan.Assignments[0], assignment => assignment is StackStorageAnchorAssignment.Capture);
        Assert.All(roots, root => Assert.True(map.Slots.ContainsKey(root)));
    }

    [Fact]
    public void MissingAllPathBackingEvidenceFailsBeforeAnchorPlanning()
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        repository.Methods[ProducerKey] = Method(ProducerKey, CliTypeIdentity.ManagedByReference(scalar));
        var method = Body(repository, Method(EntryKey, Void), [],
            I(0, CilOperation.Call, new CilOperand.Entity(ProducerKey)),
            I(1, CilOperation.Call, new CilOperand.Entity(CollectKey)), I(2, CilOperation.Pop), I(3, CilOperation.Return));
        var storage = new FixedStorage(new(
            ImmutableDictionary<int, ImmutableArray<StackStorageType>>.Empty.Add(1,
                [new(CliTypeIdentity.ManagedByReference(scalar), false) { CapturedBackings = [Pair] }]), []));
        var anchors = new RecordingAnchorPlanner();
        var analyzer = new IRootMapAnalyzer[] { new RootMapAnalyzer(repository, storage,
            new StackStorageContinuationAnalyzer(), anchors, new CollectDecision(), new CallerRootsArguments(), new RootSlotAllocator()) }[0];
        Assert.Throws<CompilerException>(() => analyzer.Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())));
        Assert.Equal(0, anchors.Calls);
    }

    private sealed class FixedStorage(StackStorageAnalysis storage) : IStackStorageTypeAnalyzer
    {
        public StackStorageAnalysis Analyze(ManagedMethodBody method) => storage;
    }

    [Fact]
    public void UnresolvedLiveByrefLocalFailsEvenWhenNoCapturedVariantWasReported()
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var address = CliTypeIdentity.ManagedByReference(scalar);
        repository.Methods[ProducerKey] = Method(ProducerKey, address);
        var method = Body(repository, Method(EntryKey, Void), [address],
            I(0, CilOperation.Call, new CilOperand.Entity(ProducerKey)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(3, CilOperation.LoadLocal, new CilOperand.Index(0)), I(4, CilOperation.Pop), I(5, CilOperation.Return));
        var storage = new FixedStorage(new([], [])
        {
            Locals = ImmutableDictionary<int, ImmutableArray<StackStorageType>>.Empty.Add(2, [new(null, false)]),
        });
        var anchors = new RecordingAnchorPlanner();
        var analyzer = new IRootMapAnalyzer[] { new RootMapAnalyzer(repository, storage,
            new StackStorageContinuationAnalyzer(), anchors, new CollectDecision(), new CallerRootsArguments(), new RootSlotAllocator()) }[0];
        Assert.Throws<CompilerException>(() => analyzer.Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())));
        Assert.Equal(0, anchors.Calls);
    }

    private sealed class RecordingAnchorPlanner : IStackStorageAnchorPlanner
    {
        public int Calls { get; private set; }

        public StackStorageAnchorPlan Plan(StackStorageAnchorRequest request)
        {
            Calls++;
            return StackStorageAnchorPlan.Empty;
        }
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(8, 0)]
    [InlineData(4, 1)]
    [InlineData(8, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    [InlineData(4, 3)]
    [InlineData(8, 3)]
    [InlineData(4, 4)]
    [InlineData(8, 4)]
    [InlineData(4, 5)]
    [InlineData(8, 5)]
    [InlineData(4, 6)]
    [InlineData(8, 6)]
    public void PreservedAggregateValuesAndTypedAddressesPublishTheirPayload(int pointerSize, int producer)
    {
        var repository = new Repository(pointerSize);
        var address = CliTypeIdentity.ManagedByReference(Pair);
        var parameter = producer == 1 ? address : Pair;
        var entry = Method(EntryKey, Void, parameter);
        var local = producer == 3 ? address : Pair;
        var produced = producer == 6 ? CliTypeIdentity.ManagedByReference(Reference) :
            producer is 1 or 2 or 3 or 4 ? address : Pair;
        repository.Methods[ProducerKey] = Method(ProducerKey, produced);
        var instruction = producer switch
        {
            0 or 1 => I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            2 => I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            3 => I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
            _ => I(0, CilOperation.Call, new CilOperand.Entity(ProducerKey)),
        };
        var method = Body(repository, entry, [local], instruction,
            I(1, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(2, CilOperation.Pop), I(3, CilOperation.Return));
        var storage = Analyze(repository, method);
        Assert.Equal(produced, Assert.Single(storage[1]).Type);
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>()));
        Assert.Contains(new RootSource(RootSourceKind.EvaluationStack, 0,
            producer == 6 ? 0 : pointerSize), roots.Safepoints[1].Roots);
        if (produced.StackKind == CliValueKind.ManagedAddress)
        {
            Assert.Contains(new RootSource(RootSourceKind.ManagedAddressEvaluationStack, 0), roots.Safepoints[1].Roots);
        }
        Assert.DoesNotContain(roots.Safepoints[1].Roots, root => root.Kind == RootSourceKind.Local);
    }

    [Fact]
    public void DuplicateAndUnrelatedOperationsPreserveEveryLowerStorageSlot()
    {
        var repository = new Repository(4);
        var method = Body(repository, Method(EntryKey, Void, Pair), [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Duplicate),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(4, CilOperation.Add), I(5, CilOperation.Pop),
            I(6, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(7, CilOperation.Pop), I(8, CilOperation.Pop), I(9, CilOperation.Return));
        var result = Analyze(repository, method);
        Assert.Equal(new[] { Pair, Pair }, result[6].Select(slot => slot.Type));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlFlowJoinsIgnoreOriginButRejectConflictingStorageIdentities(bool conflicting)
    {
        var repository = new Repository(4);
        var alternate = conflicting ? CliTypeIdentity.Named(Assembly, "Tests", "OtherPair", true) : Pair;
        var method = Body(repository, Method(EntryKey, Void, Pair, alternate), [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(5)),
            I(4, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(5, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(6, CilOperation.Pop), I(7, CilOperation.Return));
        if (conflicting)
        {
            Assert.Throws<CompilerException>(() => Analyze(repository, method));
        }
        else
        {
            Assert.Equal(Pair, Assert.Single(Analyze(repository, method)[5]).Type);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceFreeAndNativePointerMethodsNeedNoStorageMap(bool nativePointer)
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var type = nativePointer ? CliTypeIdentity.UnmanagedPointer(Pair) : CliTypeIdentity.ManagedByReference(scalar);
        var method = Body(repository, Method(EntryKey, Void, type), [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(2, CilOperation.Pop), I(3, CilOperation.Return));
        Assert.Empty(Analyze(repository, method));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void ArithmeticNeedsCompleteStableOrCapturedBackingRatherThanAReferenceFreePointee(int source, bool stable)
    {
        var repository = new Repository(4);
        var buffer = CliTypeIdentity.Named(Assembly, "Tests", "Buffer", true);
        var pointee = source == 2 ? Pair : buffer;
        var parameter = source == 3 ? CliTypeIdentity.ManagedByReference(buffer) : pointee;
        var local = source == 4 ? CliTypeIdentity.ManagedByReference(buffer) : pointee;
        var producer = source switch
        {
            1 => I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
            3 => I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            _ => I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
        };
        var method = Body(repository, Method(EntryKey, Void, parameter), [local],
            producer, I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            I(2, CilOperation.Add), I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(5, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(6, CilOperation.Pop), I(7, CilOperation.Return));
        var storage = Assert.Single(Analyze(repository, method)[5]);
        Assert.Null(storage.Type);
        Assert.Equal(stable, storage.BackingCovered);
        var request = new RootMapAnalysisRequest(method, new HashSet<EntityKey> { CollectKey }, new HashSet<string>());
        if (source != 4)
        {
            var roots = CreateRootAnalyzer(repository).Analyze(request).Safepoints[5].Roots;
            Assert.Contains(new RootSource(RootSourceKind.ManagedAddressEvaluationStack, 0), roots);
            Assert.DoesNotContain(roots, root => root.Kind == RootSourceKind.EvaluationStack);
            if (source == 2)
            {
                Assert.Contains(new RootSource(RootSourceKind.Local, 0, 4), roots);
            }
        }
        else
        {
            Assert.Throws<CompilerException>(() => CreateRootAnalyzer(repository).Analyze(request));
        }
    }

    [Fact]
    public void UncertainArithmeticConsumedBeforeCollectionDoesNotRejectTheMethod()
    {
        var repository = new Repository(4);
        var method = Body(repository, Method(EntryKey, Void, CliTypeIdentity.ManagedByReference(Pair)), [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(2, CilOperation.Add),
            I(3, CilOperation.Pop), I(4, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(5, CilOperation.Return));
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[4].Roots;
        Assert.DoesNotContain(roots, root => root.Kind is RootSourceKind.EvaluationStack or RootSourceKind.ManagedAddressEvaluationStack);
        Assert.Contains(new RootSource(RootSourceKind.ManagedAddressArgument, 0), roots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedStableAndCapturedReferenceFreeBackingsNeedNoAnchorRegardlessOfPredecessorOrder(bool reverse)
    {
        var repository = new Repository(4);
        var buffer = CliTypeIdentity.Named(Assembly, "Tests", "Buffer", true);
        var proven = CilOperation.LoadLocalAddress;
        var unproven = CilOperation.LoadLocal;
        var method = Body(repository, Method(EntryKey, Void), [CliTypeIdentity.ManagedByReference(buffer), buffer],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
            I(2, reverse ? unproven : proven, new CilOperand.Index(reverse ? 0 : 1)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(4, CilOperation.Add),
            I(5, CilOperation.Branch, new CilOperand.BranchTarget(9)),
            I(6, reverse ? proven : unproven, new CilOperand.Index(reverse ? 1 : 0)),
            I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(8, CilOperation.Add),
            I(9, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(10, CilOperation.Pop), I(11, CilOperation.Return));
        var storage = Assert.Single(Analyze(repository, method)[9]);
        Assert.Null(storage.Type);
        Assert.False(storage.BackingCovered);
        Assert.True(storage.CompleteBackingKnown);
        var map = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>()));
        Assert.Empty(map.AnchorPlan.Anchors);
        Assert.Contains(new RootSource(RootSourceKind.ManagedAddressEvaluationStack, 0), map.Safepoints[9].Roots);
    }

    [Theory]
    [InlineData(CilOperation.LoadStaticField, false)]
    [InlineData(CilOperation.LoadStaticFieldAddress, true)]
    [InlineData(CilOperation.LoadField, false)]
    [InlineData(CilOperation.LoadFieldAddress, true)]
    [InlineData(CilOperation.LoadObject, false)]
    [InlineData(CilOperation.DefaultValue, false)]
    [InlineData(CilOperation.Unbox, true)]
    [InlineData(CilOperation.UnboxAny, false)]
    [InlineData(CilOperation.LoadArrayElement, false)]
    [InlineData(CilOperation.LoadArrayElementAddress, true)]
    [InlineData(CilOperation.LoadRectangularArrayElement, false)]
    [InlineData(CilOperation.LoadRectangularArrayElementAddress, true)]
    [InlineData(CilOperation.CallVirtual, false)]
    [InlineData(CilOperation.CallIndirect, false)]
    [InlineData(CilOperation.NewObject, false)]
    public void ExplicitTypedProducersDescribeTheirAggregateStorage(CilOperation operation, bool address)
    {
        var repository = new Repository(4);
        var instructions = new List<CilInstruction>();
        CilOperand operand = new CilOperand.TypeIdentity(Pair);
        if (operation is CilOperation.LoadField or CilOperation.LoadFieldAddress or
            CilOperation.Unbox or CilOperation.UnboxAny or CilOperation.LoadArrayElement or
            CilOperation.LoadArrayElementAddress or CilOperation.LoadRectangularArrayElement or
            CilOperation.LoadRectangularArrayElementAddress or CilOperation.CallVirtual)
        {
            instructions.Add(I(instructions.Count, CilOperation.LoadNull));
        }
        if (operation is CilOperation.LoadArrayElement or CilOperation.LoadArrayElementAddress or
            CilOperation.LoadRectangularArrayElement or CilOperation.LoadRectangularArrayElementAddress)
        {
            instructions.Add(I(instructions.Count, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)));
        }
        if (operation == CilOperation.LoadObject)
        {
            instructions.Add(I(instructions.Count, CilOperation.LoadArgument, new CilOperand.Index(0)));
        }
        if (operation is CilOperation.LoadField or CilOperation.LoadFieldAddress or
            CilOperation.LoadStaticField or CilOperation.LoadStaticFieldAddress)
        {
            operand = new CilOperand.Entity(FieldKey);
        }
        if (operation is CilOperation.LoadRectangularArrayElement or CilOperation.LoadRectangularArrayElementAddress)
        {
            operand = new CilOperand.TypeIdentity(CliTypeIdentity.Array(Pair, 1));
        }
        if (operation == CilOperation.CallVirtual)
        {
            repository.Methods[ProducerKey] = Method(ProducerKey, Pair) with { IsStatic = false };
            operand = new CilOperand.MethodInstance(new(repository.Methods[ProducerKey], Owner, [], repository.Methods[ProducerKey].Signature));
        }
        if (operation == CilOperation.CallIndirect)
        {
            repository.Methods[ProducerKey] = Method(ProducerKey, Pair);
            instructions.Add(I(instructions.Count, CilOperation.LoadFunction, new CilOperand.Entity(ProducerKey)));
            operand = new CilOperand.CallSite(MethodSignatureModel.Create(Pair));
        }
        if (operation == CilOperation.NewObject)
        {
            repository.Methods[ProducerKey] = Method(ProducerKey, Void) with
            {
                IsStatic = false,
                Name = ".ctor",
                DeclaringType = PairKey,
            };
            operand = new CilOperand.Entity(ProducerKey);
        }
        instructions.Add(I(instructions.Count, operation, operand));
        var safepoint = instructions.Count;
        instructions.Add(I(safepoint, CilOperation.Call, new CilOperand.Entity(CollectKey)));
        instructions.Add(I(instructions.Count, CilOperation.Pop));
        instructions.Add(I(instructions.Count, CilOperation.Return));
        var method = Body(repository, Method(EntryKey, Void, CliTypeIdentity.ManagedByReference(Pair)), [], [.. instructions]);
        Assert.Equal(address ? CliTypeIdentity.ManagedByReference(Pair) : Pair,
            Assert.Single(Analyze(repository, method)[safepoint]).Type);
    }

    [Fact]
    public void StableLoopBackedgesPreserveTheirStorageIdentity()
    {
        var repository = new Repository(4);
        var method = Body(repository, Method(EntryKey, Void, Pair), [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(2, CilOperation.Duplicate), I(3, CilOperation.Pop),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(5, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(2)),
            I(6, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(7, CilOperation.Pop), I(8, CilOperation.Return));
        Assert.Equal(Pair, Assert.Single(Analyze(repository, method)[6]).Type);
    }

    [Fact]
    public void WholeStorageProofSurvivesFieldProjectionAndDuplicates()
    {
        var repository = new Repository(4);
        var buffer = CliTypeIdentity.Named(Assembly, "Tests", "Buffer", true);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), buffer, scalar);
        var method = Body(repository, Method(EntryKey, Void), [buffer],
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(2, CilOperation.Duplicate), I(3, CilOperation.Pop),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(5, CilOperation.Add),
            I(6, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(7, CilOperation.Pop), I(8, CilOperation.Return));
        var result = Assert.Single(Analyze(repository, method)[6]);
        Assert.Null(result.Type);
        Assert.True(result.BackingCovered);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void ProjectedAddressesPreserveCompleteOwnerRootsAfterItsLastLocalUse(int pointerSize)
    {
        var repository = new Repository(pointerSize);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var method = Body(repository, Method(EntryKey, Void), [Pair],
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(2, CilOperation.Duplicate),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(4, CilOperation.Add),
            I(5, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(6, CilOperation.Pop), I(7, CilOperation.Pop), I(8, CilOperation.Return));
        var slots = Analyze(repository, method)[5];
        Assert.All(slots, slot =>
        {
            Assert.True(slot.BackingCovered);
            Assert.Equal(new StackStorageOwner(false, 0, Pair), Assert.Single(slot.BackingOwners));
        });
        Assert.Null(slots[1].Type);
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[5].Roots;
        Assert.Contains(new RootSource(RootSourceKind.Local, 0, pointerSize), roots);
        Assert.Contains(new RootSource(RootSourceKind.ManagedAddressEvaluationStack, 0), roots);
        Assert.Contains(new RootSource(RootSourceKind.ManagedAddressEvaluationStack, 1), roots);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OwnerJoinsPreserveStableOwnersAndCaptureTheOtherPath(bool reverse, bool uncovered)
    {
        var repository = new Repository(4);
        var first = I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0));
        var second = I(6, uncovered ? CilOperation.LoadLocal : CilOperation.LoadLocalAddress,
            new CilOperand.Index(uncovered ? 2 : 1));
        var method = Body(repository, Method(EntryKey, Void), [Pair, Pair, CliTypeIdentity.ManagedByReference(Pair)],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
            reverse ? I(2, second.Operation, second.Operand) : first,
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(4, CilOperation.Add),
            I(5, CilOperation.Branch, new CilOperand.BranchTarget(9)),
            reverse ? I(6, first.Operation, first.Operand) : second,
            I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(8, CilOperation.Add),
            I(9, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(10, CilOperation.Pop), I(11, CilOperation.Return));
        var slot = Assert.Single(Analyze(repository, method)[9]);
        Assert.Null(slot.Type);
        Assert.Equal(!uncovered, slot.BackingCovered);
        Assert.Contains(new StackStorageOwner(false, 0, Pair), slot.BackingOwners);
        Assert.Equal(uncovered ? 1 : 2, slot.BackingOwners.Count);
        var request = new RootMapAnalysisRequest(method, new HashSet<EntityKey> { CollectKey }, new HashSet<string>());
        if (uncovered)
        {
            Assert.True(slot.CompleteBackingKnown);
            var map = CreateRootAnalyzer(repository).Analyze(request);
            var anchor = map.AnchorPlan.Anchors.IndexOf(new(new(StackStorageLocationKind.EvaluationStack, 0), Pair));
            Assert.True(anchor >= 0);
            Assert.Contains(new RootSource(RootSourceKind.StackStorageAnchor, anchor, 4), map.Safepoints[9].Roots);
            Assert.Contains(new RootSource(RootSourceKind.Local, 0, 4), map.Safepoints[9].Roots);
        }
        else
        {
            var roots = CreateRootAnalyzer(repository).Analyze(request).Safepoints[9].Roots;
            Assert.Contains(new RootSource(RootSourceKind.Local, 0, 4), roots);
            Assert.Contains(new RootSource(RootSourceKind.Local, 1, 4), roots);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteStableOwnersRemainValidAfterContentsAreReassigned(bool argument)
    {
        var repository = new Repository(4);
        var method = Body(repository, Method(EntryKey, Void, Pair), [Pair],
            I(0, argument ? CilOperation.LoadArgumentAddress : CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(2, CilOperation.Add),
            I(3, CilOperation.DefaultValue, new CilOperand.TypeIdentity(Pair)),
            I(4, argument ? CilOperation.StoreArgument : CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(5, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(6, CilOperation.Pop), I(7, CilOperation.Return));
        var slot = Assert.Single(Analyze(repository, method)[5]);
        Assert.True(slot.BackingCovered);
        Assert.Equal(new StackStorageOwner(argument, 0, Pair), Assert.Single(slot.BackingOwners));
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[5].Roots;
        Assert.Contains(new RootSource(argument ? RootSourceKind.Argument : RootSourceKind.Local, 0, 4), roots);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void ByReferenceParametersAndReceiversAreCoveredOnlyWhenTheirPointerSlotsAreStable(
        bool receiver, int mutation)
    {
        var repository = new Repository(4);
        var address = CliTypeIdentity.ManagedByReference(Pair);
        var entry = receiver
            ? Method(EntryKey, Void) with { IsStatic = false, DeclaringType = PairKey }
            : Method(EntryKey, Void, address);
        var instructions = new List<CilInstruction>();
        if (mutation != 0)
        {
            instructions.Add(I(0, mutation == 1 ? CilOperation.LoadArgumentAddress : CilOperation.LoadArgument,
                new CilOperand.Index(0)));
            instructions.Add(I(1, mutation == 1 ? CilOperation.Pop : CilOperation.StoreArgument,
                mutation == 1 ? new CilOperand.None() : new CilOperand.Index(0)));
        }
        instructions.Add(I(instructions.Count, CilOperation.LoadArgument, new CilOperand.Index(0)));
        instructions.Add(I(instructions.Count, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)));
        instructions.Add(I(instructions.Count, CilOperation.Add));
        var safepoint = instructions.Count;
        instructions.Add(I(safepoint, CilOperation.Call, new CilOperand.Entity(CollectKey)));
        instructions.Add(I(instructions.Count, CilOperation.Pop));
        instructions.Add(I(instructions.Count, CilOperation.Return));
        var method = Body(repository, entry, [], [.. instructions]);
        var slot = Assert.Single(Analyze(repository, method)[safepoint]);
        Assert.Null(slot.Type);
        Assert.Equal(mutation == 0, slot.BackingCovered);
        var request = new RootMapAnalysisRequest(method, new HashSet<EntityKey> { CollectKey }, new HashSet<string>());
        if (mutation == 0)
        {
            Assert.Equal(new StackStorageOwner(true, 0, address), Assert.Single(slot.BackingOwners));
            var roots = CreateRootAnalyzer(repository).Analyze(request).Safepoints[safepoint].Roots;
            Assert.Contains(new RootSource(RootSourceKind.ManagedAddressArgument, 0, 4), roots);
            Assert.Contains(new RootSource(RootSourceKind.ManagedAddressEvaluationStack, 0), roots);
        }
        else
        {
            Assert.Empty(slot.BackingOwners);
            Assert.True(slot.CompleteBackingKnown);
            var map = CreateRootAnalyzer(repository).Analyze(request);
            var anchor = Assert.Single(map.AnchorPlan.Anchors);
            Assert.Equal(new StackStorageAnchor(new(StackStorageLocationKind.EvaluationStack, 0), Pair), anchor);
            Assert.Contains(new RootSource(RootSourceKind.StackStorageAnchor, 0, 4), map.Safepoints[safepoint].Roots);
            Assert.Contains(map.AnchorPlan.Assignments.Values.SelectMany(assignments => assignments),
                assignment => assignment is StackStorageAnchorAssignment.Capture);
        }
    }

    [Fact]
    public void ClosedMethodArgumentsSubstituteLocalStorageTypes()
    {
        var repository = new Repository(4);
        var parameter = CliTypeIdentity.GenericParameter(method: true, 0);
        var definition = Method(EntryKey, Void) with { GenericArity = 1 };
        var instance = new MethodInstanceModel(definition, Owner, [Pair], definition.Signature);
        var body = new CilMethodBody(definition, 2, [CliValueKind.ValueType],
            [I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
             I(1, CilOperation.LoadObject, new CilOperand.TypeIdentity(Pair)),
             I(2, CilOperation.Call, new CilOperand.Entity(CollectKey)),
             I(3, CilOperation.Pop), I(4, CilOperation.Return)])
        {
            MethodInstance = instance,
            LocalSignatureTypes = [parameter],
        };
        var graph = ((IControlFlowGraphBuilderFactory)new ControlFlowGraphBuilderFactory()).Create().Build(body);
        var validated = new TypedStackValidator(repository, repository, repository, new StackTypeCompatibilityValidator()).Validate(graph);
        var method = new ManagedMethodBody(instance, validated);
        var result = Analyze(repository, method);
        Assert.Equal(CliTypeIdentity.ManagedByReference(Pair), Assert.Single(result[1]).Type);
        Assert.Equal(Pair, Assert.Single(result[2]).Type);
    }

    [Fact]
    public void OpenGenericStorageIsRejectedBeforeLayoutTracking()
    {
        var repository = new Repository(4);
        var parameter = CliTypeIdentity.GenericParameter(method: true, 0);
        var definition = Method(EntryKey, Void) with { GenericArity = 1 };
        var method = Body(repository, definition, [parameter],
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(1, CilOperation.Pop), I(2, CilOperation.Return));

        Assert.Throws<CompilerException>(() => AnalyzeStorage(repository, method));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void InstanceValueArgumentsUseParameterIndexAfterTheReferenceReceiver(int pointerSize)
    {
        var repository = new Repository(pointerSize);
        var entry = Method(EntryKey, Void, Pair) with { IsStatic = false };
        var method = Body(repository, entry, [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)), I(1, CilOperation.Pop),
            I(2, CilOperation.LoadArgumentAddress, new CilOperand.Index(1)),
            I(3, CilOperation.Call, new CilOperand.Entity(CollectKey)), I(4, CilOperation.Pop), I(5, CilOperation.Return));

        var result = AnalyzeStorage(repository, method);

        var slot = Assert.Single(result.Entries[3]);
        Assert.Equal(CliTypeIdentity.ManagedByReference(Pair), slot.Type);
        Assert.Equal(new StackStorageOwner(true, 1, Pair), Assert.Single(slot.BackingOwners));
        Assert.True(slot.BackingCovered);
    }

    [Fact]
    public void RvaSpanProducerRetainsItsDeclaredValueType()
    {
        var repository = new Repository(4);
        repository.Methods[ProducerKey] = Method(ProducerKey, Pair);
        var element = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var span = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "ReadOnlySpan`1", true), [element]);
        repository.Layouts.Add(span, new(span, 8, 4, []) { ByReferenceOffsets = [0] });
        var field = new FieldDefinitionModel(FieldKey, OwnerKey, "Data", element, true)
        { InitialData = [1, 0, 0, 0] };
        var method = Body(repository, Method(EntryKey, Void), [],
            I(0, CilOperation.Call, new CilOperand.Entity(ProducerKey)), I(1, CilOperation.Pop),
            I(2, CilOperation.CreateReadOnlySpanFromRva,
                new CilOperand.ReadOnlySpanData(new(field, Owner, element), span, element, 1)),
            I(3, CilOperation.Call, new CilOperand.Entity(CollectKey)), I(4, CilOperation.Pop), I(5, CilOperation.Return));

        Assert.Equal(span, Assert.Single(AnalyzeStorage(repository, method).Entries[3]).Type);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void CustomModifiersDoNotChangeTrackedPointeeAndAggregateStorage(int pointerSize)
    {
        var repository = new Repository(pointerSize);
        var byRef = CliTypeIdentity.ManagedByReference(Pair);
        var modifier = CliTypeIdentity.Named(Assembly, "Tests", "Modifier", false);
        var local = CliTypeIdentity.Modified(byRef, modifier, true);
        var value = CliTypeIdentity.Modified(Pair, modifier, false);
        var method = Body(repository, Method(EntryKey, Void, byRef), [local],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadObject, new CilOperand.TypeIdentity(value)),
            I(4, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(5, CilOperation.Pop), I(6, CilOperation.Return));

        var result = AnalyzeStorage(repository, method);

        Assert.Equal(byRef, Assert.Single(result.Entries[3]).Type);
        Assert.Equal(Pair, Assert.Single(result.Entries[4]).Type);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(4, true)]
    [InlineData(8, true)]
    public void UnstableCompletePointersCaptureTheirBaseAndTransferItAcrossDepthChanges(
        int pointerSize, bool returned)
    {
        var repository = new Repository(pointerSize);
        var address = CliTypeIdentity.ManagedByReference(Pair);
        repository.Methods[ProducerKey] = Method(ProducerKey, address);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var producer = returned
            ? I(1, CilOperation.Call, new CilOperand.Entity(ProducerKey))
            : I(1, CilOperation.LoadLocal, new CilOperand.Index(0));
        var method = Body(repository, Method(EntryKey, Void), [address],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), producer,
            I(2, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(3, CilOperation.Add), I(4, CilOperation.Duplicate),
            I(5, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(6, CilOperation.Pop), I(7, CilOperation.Pop), I(8, CilOperation.Return));
        var analysis = AnalyzeStorage(repository, method);
        Assert.All(analysis.Entries[5], slot =>
        {
            Assert.Null(slot.Type);
            Assert.False(slot.BackingCovered);
            Assert.Empty(slot.BackingOwners);
            Assert.Equal(Pair, Assert.Single(slot.CapturedBackings));
        });
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.EvaluationStack, 1),
            analysis.Transfers[1].Destination);
        if (returned)
        {
            Assert.Equal(Pair, Assert.Single(analysis.Transfers[1].Captures));
            Assert.Empty(analysis.Transfers[1].Copies);
        }
        else
        {
            Assert.Empty(analysis.Transfers[1].Captures);
            Assert.Equal(new StackStorageLocation(StackStorageLocationKind.Local, 0),
                analysis.Transfers[1].Copies[Pair]);
        }
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.EvaluationStack, 1),
            analysis.Transfers[2].Copies[Pair]);
        Assert.Empty(analysis.Transfers[2].Captures);
        Assert.Equal(0, analysis.Transfers[3].Destination.Index);
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.EvaluationStack, 1),
            analysis.Transfers[3].Copies[Pair]);
        Assert.Equal(1, analysis.Transfers[4].Destination.Index);
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.EvaluationStack, 0),
            analysis.Transfers[4].Copies[Pair]);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void ProjectedPointerJoinsRetainSeparateClosedBackingVariants(int pointerSize)
    {
        var repository = new Repository(pointerSize);
        var other = CliTypeIdentity.Named(Assembly, "Tests", "OtherPair", true);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var firstField = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var otherField = firstField with { DeclaringType = other };
        var method = Body(repository, Method(EntryKey, Void),
            [CliTypeIdentity.ManagedByReference(Pair), CliTypeIdentity.ManagedByReference(other)],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(7)),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(firstField)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(5, CilOperation.Add),
            I(6, CilOperation.Branch, new CilOperand.BranchTarget(11)),
            I(7, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(8, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(otherField)),
            I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(10, CilOperation.Add),
            I(11, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(12, CilOperation.Pop), I(13, CilOperation.Return));
        var analysis = AnalyzeStorage(repository, method);
        var joined = Assert.Single(analysis.Entries[11]);
        Assert.Null(joined.Type);
        Assert.True(joined.CapturedBackings.SetEquals([Pair, other]));
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.Local, 0),
            analysis.Transfers[2].Copies[Pair]);
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.Local, 1),
            analysis.Transfers[7].Copies[other]);
        Assert.Equal(0, analysis.Transfers[5].Copies[Pair].Index);
        Assert.Equal(0, analysis.Transfers[10].Copies[other].Index);
    }

    [Fact]
    public void EveryReplacementDeclaresInactiveVariantsEvenForScalarResults()
    {
        var repository = new Repository(4);
        var address = CliTypeIdentity.ManagedByReference(Pair);
        var method = Body(repository, Method(EntryKey, Void), [address],
            I(0, CilOperation.LoadLocal, new CilOperand.Index(0)), I(1, CilOperation.Pop),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(3, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(4, CilOperation.Pop), I(5, CilOperation.Return));
        var analysis = AnalyzeStorage(repository, method);
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.Local, 0),
            analysis.Transfers[0].Copies[Pair]);
        var scalar = analysis.Transfers[2];
        Assert.Equal(0, scalar.Destination.Index);
        Assert.Empty(scalar.Captures);
        Assert.Empty(scalar.Copies);
        Assert.Empty(analysis.Entries[3][0].CapturedBackings);
        Assert.DoesNotContain(1, analysis.Transfers.Keys);
        Assert.DoesNotContain(3, analysis.Transfers.Keys);
    }

    [Theory]
    [InlineData(4, false, false)]
    [InlineData(8, false, false)]
    [InlineData(4, false, true)]
    [InlineData(8, false, true)]
    [InlineData(4, true, false)]
    [InlineData(8, true, false)]
    [InlineData(4, true, true)]
    [InlineData(8, true, true)]
    public void ProjectedByrefLocalsPreserveStableOwnersOrCapturedBasesBeforeAndAfterReload(
        int pointerSize, bool captured, bool reloadBeforeCollection)
    {
        var repository = new Repository(pointerSize);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var address = CliTypeIdentity.ManagedByReference(scalar);
        repository.Methods[ProducerKey] = Method(ProducerKey, CliTypeIdentity.ManagedByReference(Pair));
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var instructions = new List<CilInstruction>
        {
            captured ? I(0, CilOperation.Call, new CilOperand.Entity(ProducerKey))
                : I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(1)),
        };
        if (reloadBeforeCollection) instructions.Add(I(3, CilOperation.LoadLocal, new CilOperand.Index(1)));
        var safepoint = instructions.Count;
        instructions.Add(I(safepoint, CilOperation.Call, new CilOperand.Entity(CollectKey)));
        var reload = reloadBeforeCollection ? 3 : instructions.Count;
        if (!reloadBeforeCollection) instructions.Add(I(reload, CilOperation.LoadLocal, new CilOperand.Index(1)));
        instructions.Add(I(instructions.Count, CilOperation.Pop));
        instructions.Add(I(instructions.Count, CilOperation.Return));
        var method = Body(repository, Method(EntryKey, Void), [Pair, address], [.. instructions]);
        var analysis = AnalyzeStorage(repository, method);
        var local = analysis.Locals[safepoint][1];
        Assert.Equal(address, local.Type);
        Assert.Equal(!captured, local.BackingCovered);
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.Local, 1), analysis.Transfers[2].Destination);
        Assert.Equal(new StackStorageLocation(StackStorageLocationKind.EvaluationStack, 0),
            analysis.Transfers[reload].Destination);
        if (captured)
        {
            Assert.Empty(local.BackingOwners);
            Assert.Equal(Pair, Assert.Single(local.CapturedBackings));
            Assert.Equal(new StackStorageLocation(StackStorageLocationKind.EvaluationStack, 0),
                analysis.Transfers[2].Copies[Pair]);
            Assert.Equal(new StackStorageLocation(StackStorageLocationKind.Local, 1),
                analysis.Transfers[reload].Copies[Pair]);
        }
        else
        {
            Assert.Empty(local.CapturedBackings);
            Assert.Equal(new StackStorageOwner(false, 0, Pair), Assert.Single(local.BackingOwners));
            var roots = CreateRootAnalyzer(repository).Analyze(new(method,
                new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[safepoint].Roots;
            Assert.Contains(new RootSource(RootSourceKind.Local, 0, pointerSize), roots);
            Assert.Contains(new RootSource(reloadBeforeCollection ? RootSourceKind.ManagedAddressEvaluationStack
                : RootSourceKind.ManagedAddressLocal, reloadBeforeCollection ? 0 : 1), roots);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByrefLocalReassignmentReplacesBackingOwnersRatherThanRetainingTheOldOwner(bool live)
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var instructions = new List<CilInstruction>
        {
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(3, CilOperation.LoadLocalAddress, new CilOperand.Index(2)),
            I(4, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(5, CilOperation.StoreLocal, new CilOperand.Index(1)),
        };
        if (!live)
        {
            instructions.Add(I(6, CilOperation.LoadLocal, new CilOperand.Index(1)));
            instructions.Add(I(7, CilOperation.Pop));
        }
        var safepoint = instructions.Count;
        instructions.Add(I(safepoint, CilOperation.Call, new CilOperand.Entity(CollectKey)));
        if (live)
        {
            instructions.Add(I(instructions.Count, CilOperation.LoadLocal, new CilOperand.Index(1)));
            instructions.Add(I(instructions.Count, CilOperation.Pop));
        }
        instructions.Add(I(instructions.Count, CilOperation.Return));
        var method = Body(repository, Method(EntryKey, Void),
            [Pair, CliTypeIdentity.ManagedByReference(scalar), Pair], [.. instructions]);
        var analysis = AnalyzeStorage(repository, method);
        Assert.Equal(new StackStorageOwner(false, 2, Pair), Assert.Single(analysis.Locals[safepoint][1].BackingOwners));
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[safepoint].Roots;
        Assert.DoesNotContain(new RootSource(RootSourceKind.Local, 0, 4), roots);
        Assert.Equal(live, roots.Contains(new RootSource(RootSourceKind.Local, 2, 4)));
    }

    [Fact]
    public void ByrefLocalJoinsUnionOnlyTheCurrentIncomingOwners()
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var method = Body(repository, Method(EntryKey, Void),
            [Pair, CliTypeIdentity.ManagedByReference(scalar), Pair],
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(4, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(8)),
            I(5, CilOperation.LoadLocalAddress, new CilOperand.Index(2)),
            I(6, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(7, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(8, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(9, CilOperation.LoadLocal, new CilOperand.Index(1)), I(10, CilOperation.Pop), I(11, CilOperation.Return));
        var analysis = AnalyzeStorage(repository, method);
        Assert.True(analysis.Locals[8][1].BackingOwners.SetEquals(
            [new(false, 0, Pair), new(false, 2, Pair)]));
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[8].Roots;
        Assert.Contains(new RootSource(RootSourceKind.Local, 0, 4), roots);
        Assert.Contains(new RootSource(RootSourceKind.Local, 2, 4), roots);
    }

    [Fact]
    public void AcceptedFilterHandlerPreservesTheProjectedLocalOwnerEstablishedBeforeTheTry()
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var method = Body(repository, Method(EntryKey, Void), [Pair, CliTypeIdentity.ManagedByReference(scalar)],
            [I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
             I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
             I(2, CilOperation.StoreLocal, new CilOperand.Index(1)),
             I(3, CilOperation.LoadNull), I(4, CilOperation.Throw),
             I(5, CilOperation.Pop), I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
             I(7, CilOperation.EndFilter), I(8, CilOperation.Pop),
             I(9, CilOperation.Call, new CilOperand.Entity(CollectKey)),
             I(10, CilOperation.LoadLocal, new CilOperand.Index(1)), I(11, CilOperation.Pop),
             I(12, CilOperation.Leave, new CilOperand.BranchTarget(13)), I(13, CilOperation.Return)],
            [new(CilExceptionRegionKind.Filter, 3, 2, 8, 5, null, 5)]);
        var analysis = AnalyzeStorage(repository, method);
        Assert.Equal(new StackStorageOwner(false, 0, Pair), Assert.Single(analysis.Locals[9][1].BackingOwners));
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[9].Roots;
        Assert.Contains(new RootSource(RootSourceKind.Local, 0, 4), roots);
    }

    [Fact]
    public void FilterSafepointDemandsTheCapturedBackingStoredInAByrefLocal()
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        repository.Methods[ProducerKey] = Method(
            ProducerKey,
            CliTypeIdentity.ManagedByReference(Pair));
        var field = new FieldInstanceModel(
            new(FieldKey, PairKey, "First", scalar, false),
            Pair,
            scalar);
        var method = Body(
            repository,
            Method(EntryKey, Void),
            [CliTypeIdentity.ManagedByReference(scalar)],
            [
                I(0, CilOperation.Call, new CilOperand.Entity(ProducerKey)),
                I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
                I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(3, CilOperation.LoadNull),
                I(4, CilOperation.Throw),
                I(5, CilOperation.Pop),
                I(6, CilOperation.Call, new CilOperand.Entity(CollectKey)),
                I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(8, CilOperation.EndFilter),
                I(9, CilOperation.Pop),
                I(10, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(11, CilOperation.Pop),
                I(12, CilOperation.Leave, new CilOperand.BranchTarget(13)),
                I(13, CilOperation.Return),
            ],
            [new(CilExceptionRegionKind.Filter, 3, 2, 9, 4, null, 5)]);

        var map = CreateRootAnalyzer(repository).Analyze(new(
            method,
            new HashSet<EntityKey> { CollectKey },
            new HashSet<string>()));

        var anchor = new StackStorageAnchor(
            new(StackStorageLocationKind.Local, 0),
            Pair);
        var index = map.AnchorPlan.Anchors.IndexOf(anchor);
        Assert.True(index >= 0);
        Assert.Contains(
            new RootSource(RootSourceKind.StackStorageAnchor, index),
            map.Safepoints[6].Roots);
        Assert.Contains(
            new RootSource(RootSourceKind.StackStorageAnchor, index, 4),
            map.Safepoints[6].Roots);
    }

    [Fact]
    public void FinallyReassignmentReachesTheNormalLeaveContinuationAndReplacesItsOwner()
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var method = Body(repository, Method(EntryKey, Void),
            [Pair, CliTypeIdentity.ManagedByReference(scalar), Pair],
            [I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
             I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
             I(2, CilOperation.StoreLocal, new CilOperand.Index(1)), I(3, CilOperation.Nop),
             I(4, CilOperation.Leave, new CilOperand.BranchTarget(9)),
             I(5, CilOperation.LoadLocalAddress, new CilOperand.Index(2)),
             I(6, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
             I(7, CilOperation.StoreLocal, new CilOperand.Index(1)), I(8, CilOperation.EndFinally),
             I(9, CilOperation.Call, new CilOperand.Entity(CollectKey)),
             I(10, CilOperation.LoadLocal, new CilOperand.Index(1)), I(11, CilOperation.Pop), I(12, CilOperation.Return)],
            [new(CilExceptionRegionKind.Finally, 3, 2, 5, 4, null, null)]);
        var analysis = AnalyzeStorage(repository, method);
        Assert.Equal(new StackStorageOwner(false, 2, Pair), Assert.Single(analysis.Locals[9][1].BackingOwners));
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[9].Roots;
        Assert.Contains(new RootSource(RootSourceKind.Local, 2, 4), roots);
        Assert.DoesNotContain(new RootSource(RootSourceKind.Local, 0, 4), roots);
    }

    [Fact]
    public void UnreachableLeaveDoesNotInjectAnUnvalidatedContinuationIntoLocalStorageFlow()
    {
        var repository = new Repository(4);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new(FieldKey, PairKey, "First", scalar, false), Pair, scalar);
        var method = Body(repository, Method(EntryKey, Void), [Pair, CliTypeIdentity.ManagedByReference(scalar)],
            [I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
             I(1, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
             I(2, CilOperation.StoreLocal, new CilOperand.Index(1)),
             I(3, CilOperation.Branch, new CilOperand.BranchTarget(5)),
             I(4, CilOperation.Leave, new CilOperand.BranchTarget(13)),
             I(5, CilOperation.Call, new CilOperand.Entity(CollectKey)),
             I(6, CilOperation.Leave, new CilOperand.BranchTarget(9)),
             I(7, CilOperation.Nop), I(8, CilOperation.EndFinally),
             I(9, CilOperation.Call, new CilOperand.Entity(CollectKey)),
             I(10, CilOperation.LoadLocal, new CilOperand.Index(1)), I(11, CilOperation.Pop),
             I(12, CilOperation.Return), I(13, CilOperation.Return)],
            [new(CilExceptionRegionKind.Finally, 3, 4, 7, 2, null, null)]);
        Assert.False(method.ControlFlow.EntryStacks.ContainsKey(method.ControlFlow.Graph.GetBlockAtOffset(13).Index));
        var analysis = AnalyzeStorage(repository, method);
        Assert.False(analysis.Entries.ContainsKey(13));
        Assert.Equal(new StackStorageOwner(false, 0, Pair), Assert.Single(analysis.Locals[9][1].BackingOwners));
        var roots = CreateRootAnalyzer(repository).Analyze(new(method,
            new HashSet<EntityKey> { CollectKey }, new HashSet<string>())).Safepoints[9].Roots;
        Assert.Contains(new RootSource(RootSourceKind.Local, 0, 4), roots);
    }

    [Fact]
    public void NoSafepointMethodDoesNotInvokeStackStorageAnalysis()
    {
        var repository = new Repository(4);
        var method = Body(repository, Method(EntryKey, Void), [], I(0, CilOperation.Return));
        var storage = new RecordingStorage();
        var analyzer = new[] { new RootMapAnalyzer(repository, storage, new StackStorageContinuationAnalyzer(),
            new StackStorageAnchorPlanner(new StackStorageContinuationAnalyzer()),
            new CollectDecision(), new CallerRootsArguments(), new RootSlotAllocator()) }
            .Cast<IRootMapAnalyzer>().Single();
        Assert.Empty(analyzer.Analyze(new(method, new HashSet<EntityKey>(), new HashSet<string>())).Safepoints);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public void ConstructorAndAnalysisRejectMissingCapabilitiesAndInput()
    {
        var repository = new Repository(4);
        var continuations = new StackStorageContinuationAnalyzer();
        Assert.Throws<ArgumentNullException>(() => new StackStorageTypeAnalyzer(null!, repository, repository, repository, continuations));
        Assert.Throws<ArgumentNullException>(() => new StackStorageTypeAnalyzer(repository, null!, repository, repository, continuations));
        Assert.Throws<ArgumentNullException>(() => new StackStorageTypeAnalyzer(repository, repository, null!, repository, continuations));
        Assert.Throws<ArgumentNullException>(() => new StackStorageTypeAnalyzer(repository, repository, repository, null!, continuations));
        Assert.Throws<ArgumentNullException>(() => new StackStorageTypeAnalyzer(repository, repository, repository, repository, null!));
        Assert.Throws<ArgumentNullException>(() => Analyze(repository, null!));
    }

    private sealed class RecordingStorage : IStackStorageTypeAnalyzer
    {
        public int Calls { get; private set; }

        public StackStorageAnalysis Analyze(ManagedMethodBody method)
        {
            Calls++;
            return StackStorageAnalysis.Empty;
        }
    }

    private static ImmutableDictionary<int, ImmutableArray<StackStorageType>> Analyze(Repository repository, ManagedMethodBody method)
        => AnalyzeStorage(repository, method).Entries;

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void DetachedCatchRetainsItsExceptionStackAndDeclaredLocalStorage(int pointerSize)
    {
        var repository = new Repository(pointerSize);
        var byref = CliTypeIdentity.ManagedByReference(Pair);
        var method = Body(repository, Method(EntryKey, Void), [Pair, byref],
            [I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
             I(1, CilOperation.Pop), I(2, CilOperation.Leave, new CilOperand.BranchTarget(7)),
             I(3, CilOperation.Pop), I(4, CilOperation.LoadLocal, new CilOperand.Index(1)),
             I(5, CilOperation.Pop), I(6, CilOperation.Leave, new CilOperand.BranchTarget(7)),
             I(7, CilOperation.Return)],
            [new(CilExceptionRegionKind.Catch, 0, 3, 3, 4, OwnerKey, null)]);

        var analysis = AnalyzeStorage(repository, method);
        Assert.Null(Assert.Single(analysis.Entries[3]).Type);
        Assert.Empty(analysis.Entries[4]);
        Assert.Equal(byref, Assert.Single(analysis.Entries[5]).Type);
        Assert.Equal(byref, analysis.Locals[3][1].Type);
        Assert.Empty(analysis.Entries[7]);
    }

    [Theory]
    [InlineData(4, false, false)]
    [InlineData(8, false, false)]
    [InlineData(4, true, false)]
    [InlineData(8, true, false)]
    [InlineData(4, false, true)]
    [InlineData(8, false, true)]
    [InlineData(4, true, true)]
    [InlineData(8, true, true)]
    public void DetachedExceptionEntriesRequireReachableProtectedStorage(
        int pointerSize, bool filter, bool reachable)
    {
        var repository = new Repository(pointerSize);
        var byref = CliTypeIdentity.ManagedByReference(Pair);
        var handler = filter ? 8 : 5;
        var end = handler + 4;
        var instructions = new List<CilInstruction>
        {
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(1, CilOperation.Pop),
            reachable ? I(2, CilOperation.Nop) : I(2, CilOperation.Branch, new CilOperand.BranchTarget(end)),
            I(3, CilOperation.Nop),
            I(4, CilOperation.Leave, new CilOperand.BranchTarget(end)),
        };
        if (filter)
        {
            instructions.Add(I(5, CilOperation.Pop));
            instructions.Add(I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)));
            instructions.Add(I(7, CilOperation.EndFilter));
        }
        instructions.Add(I(handler, CilOperation.Pop));
        instructions.Add(I(handler + 1, CilOperation.LoadLocal, new CilOperand.Index(1)));
        instructions.Add(I(handler + 2, CilOperation.Pop));
        instructions.Add(I(handler + 3, CilOperation.Leave, new CilOperand.BranchTarget(end)));
        instructions.Add(I(end, CilOperation.Return));
        var method = Body(repository, Method(EntryKey, Void), [Pair, byref], [.. instructions],
            [new(filter ? CilExceptionRegionKind.Filter : CilExceptionRegionKind.Catch,
                3, 2, handler, 4, filter ? null : OwnerKey, filter ? 5 : null)]);

        var analysis = AnalyzeStorage(repository, method);

        Assert.Equal(reachable, analysis.Entries.ContainsKey(handler));
        Assert.Equal(reachable, analysis.Locals.ContainsKey(handler));
        if (filter) Assert.Equal(reachable, analysis.Entries.ContainsKey(5));
        if (reachable)
        {
            Assert.Null(Assert.Single(analysis.Entries[handler]).Type);
            Assert.Equal(byref, analysis.Locals[handler][1].Type);
            Assert.Equal(byref, Assert.Single(analysis.Entries[handler + 2]).Type);
        }
        Assert.All(analysis.Entries.Keys, offset =>
            Assert.Contains(offset, method.ControlFlow.InstructionEntryStacks.Keys));
        Assert.Empty(analysis.Entries[end]);
    }

    // `ref T local = ref *pointer` stores an unmanaged pointer: the local keeps
    // its declared type, and the memory it addresses has no backing to root.
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void NativeAddressStoredInByrefLocalKeepsItsDeclaredTypeWithNoBackingToRoot(int pointerSize)
    {
        var repository = new Repository(pointerSize);
        var method = Body(repository, Method(EntryKey, Void), [CliTypeIdentity.ManagedByReference(Pair)],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.ConvertNativeInt), I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(4, CilOperation.Call, new CilOperand.Entity(CollectKey)),
            I(5, CilOperation.Pop), I(6, CilOperation.Return));

        var analysis = AnalyzeStorage(repository, method);
        var slot = Assert.Single(analysis.Entries[4]);
        Assert.Equal(CliTypeIdentity.ManagedByReference(Pair), slot.Type);
        Assert.True(slot.BackingCovered);
        Assert.True(slot.CompleteBackingKnown);
        Assert.Empty(slot.BackingOwners);
        Assert.Empty(slot.CapturedBackings);
    }

    private static StackStorageAnalysis AnalyzeStorage(Repository repository, ManagedMethodBody method)
    {
        var analyzer = new[] { new StackStorageTypeAnalyzer(repository, repository, repository, repository,
            new StackStorageContinuationAnalyzer()) }
            .Cast<IStackStorageTypeAnalyzer>().Single();
        return analyzer.Analyze(method);
    }

    private static IRootMapAnalyzer CreateRootAnalyzer(Repository repository) =>
        new IRootMapAnalyzer[] { new RootMapAnalyzer(repository,
            new StackStorageTypeAnalyzer(repository, repository, repository, repository, new StackStorageContinuationAnalyzer()),
            new StackStorageContinuationAnalyzer(),
            new StackStorageAnchorPlanner(new StackStorageContinuationAnalyzer()),
            new CollectDecision(), new CallerRootsArguments(), new RootSlotAllocator()) }[0];

    private static ManagedMethodBody Body(Repository repository, MethodDefinitionModel entry,
        ImmutableArray<CliTypeIdentity> locals, params CilInstruction[] instructions)
        => Body(repository, entry, locals, instructions, []);

    private static ManagedMethodBody Body(Repository repository, MethodDefinitionModel entry,
        ImmutableArray<CliTypeIdentity> locals, CilInstruction[] instructions,
        ImmutableArray<CilExceptionRegion> regions)
    {
        var instance = new MethodInstanceModel(entry, entry.DeclaringType == PairKey ? Pair : Owner, [], entry.Signature);
        var body = new CilMethodBody(entry, 8, [.. locals.Select(type => type.StackKind)], [.. instructions])
        {
            MethodInstance = instance,
            LocalSignatureTypes = locals,
            ExceptionRegions = regions,
        };
        var graph = ((IControlFlowGraphBuilderFactory)new ControlFlowGraphBuilderFactory()).Create().Build(body);
        var validated = new TypedStackValidator(repository, repository, repository, new StackTypeCompatibilityValidator()).Validate(graph);
        return new(instance, validated);
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static MethodDefinitionModel Method(EntityKey key, CliTypeIdentity result, params CliTypeIdentity[] parameters) =>
        new(key, OwnerKey, "Method", true, new(result, [.. parameters]), 1);

    private sealed class CollectDecision : IRootDecisionClassifier
    {
        public bool Decide(RootDecisionRequest request) => request.Instruction.Operand is CilOperand.Entity entity && entity.Key == CollectKey;
    }

    private sealed class CallerRootsArguments : ICalleeArgumentRootingClassifier
    {
        public int Classify(RootDecisionRequest request) => 0;
    }

    private sealed class Repository(int pointerSize) : ITypeRepository, IFieldRepository, IMethodRepository, IValueLayoutProvider
    {
        public Dictionary<CliTypeIdentity, ValueLayout> Layouts { get; } = [];
        public Dictionary<EntityKey, MethodDefinitionModel> Methods { get; } = new()
        {
            [CollectKey] = Method(CollectKey, Void),
        };

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            new(key, "Tests", key == PairKey ? "Pair" : "Owner", key == PairKey, [], []);

        public FieldDefinitionModel GetField(EntityKey key) => new(key, OwnerKey, "Pair", Pair, true);

        public MethodDefinitionModel GetMethod(EntityKey key) => Methods[key];

        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            Layouts.TryGetValue(type, out var layout) ? layout : new(type, pointerSize * 2, pointerSize,
                type.StackKind == CliValueKind.ValueType && type.FullName != "Tests.Buffer" ? [pointerSize] : []);
    }
}
