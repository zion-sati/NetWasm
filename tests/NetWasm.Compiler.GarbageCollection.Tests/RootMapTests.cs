using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using StructuredMethod = NetWasm.Compiler.ControlFlow.ManagedMethodBody;

namespace NetWasm.Compiler.GarbageCollection.Tests;

public sealed class RootMapTests
{
    private static readonly AssemblyIdentity Assembly = new("Roots");
    private static readonly EntityKey TypeKey = Key(0x02000001);
    private static readonly EntityKey EntryKey = Key(0x06000001);
    private static readonly EntityKey AllocatorKey = Key(0x06000002);
    private static readonly EntityKey ConstructorKey = Key(0x06000003);
    private static readonly EntityKey LeafKey = Key(0x06000004);
    private static readonly EntityKey ExternalKey = Key(0x06000005);
    private static readonly EntityKey StaticInitializerKey = Key(0x06000006);
    private static readonly EntityKey
      JSImportKey = Key(0x06000007);
    private static readonly EntityKey PairCallKey = Key(0x06000008);
    private static readonly EntityKey PairVirtualKey = Key(0x06000009);
    private static readonly EntityKey AddressCallKey = Key(0x0600000a);
    private static readonly EntityKey ReferenceVirtualKey = Key(0x0600000b);
    private static readonly EntityKey StaticFieldKey = Key(0x04000001);
    private static readonly EntityKey UninitializedStaticFieldKey = Key(0x04000002);
    private static readonly EntityKey GenericTypeKey = Key(0x02000002);
    private static readonly EntityKey ValueTypeKey = Key(0x02000003);
    private static readonly CliTypeIdentity PairType =
        CliTypeIdentity.Named(Assembly, "Roots", "Pair", isValueType: true);

    [Fact]
    public void AllocationCapabilityPropagatesTransitivelyButNotToLeafMethods()
    {
        var program = new FakeProgram();
        var methods = new Dictionary<EntityKey, StructuredMethod>
        {
            [EntryKey] = Structured(program, Method(EntryKey),
                I(0, CilOperation.LoadString, new CilOperand.UserString("root")),
                I(1, CilOperation.Call, new CilOperand.Entity(AllocatorKey)),
                I(2, CilOperation.Return)),
            [AllocatorKey] = StructuredWithLocals(
                program,
                Method(AllocatorKey),
                [CliValueKind.ManagedReference],
                1,
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
                I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(3, CilOperation.Return)),
            [ConstructorKey] = Structured(program, Method(ConstructorKey, isStatic: false, name: ".ctor"),
                I(0, CilOperation.Return)),
            [LeafKey] = Structured(program, Method(LeafKey), I(0, CilOperation.Return)),
        }.ToImmutableDictionary();

        var allocationAnalyzer = CreateAllocationAnalyzer(program);
        var capabilities = allocationAnalyzer.Analyze(
            new AllocationCapabilityAnalysisRequest(
                methods,
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(AllocatorKey, capabilities.Methods);
        Assert.Contains(EntryKey, capabilities.Methods);
        Assert.DoesNotContain(ConstructorKey, capabilities.Methods);
        Assert.DoesNotContain(LeafKey, capabilities.Methods);
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzer(
                null!,
                program,
                program,
                new RuntimeAllocationSafepointClassifier()));
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzer(
                program,
                null!,
                program,
                new RuntimeAllocationSafepointClassifier()));
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzer(
                program,
                program,
                null!,
                new RuntimeAllocationSafepointClassifier()));
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzer(program, program, program, null!));
        Assert.Throws<ArgumentNullException>(() =>
            allocationAnalyzer.Analyze(null!));
        Assert.Throws<ArgumentNullException>(() =>
            allocationAnalyzer.Analyze(new AllocationCapabilityAnalysisRequest(
                null!,
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty)));
        Assert.Throws<ArgumentNullException>(() =>
            allocationAnalyzer.Analyze(new AllocationCapabilityAnalysisRequest(
                ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
                ImmutableDictionary<string, StructuredMethod>.Empty,
                null!)));
    }

    [Fact]
    public void ConstructedAllocationCapabilityAndRootMapsUseCanonicalMethodIdentity()
    {
        var program = new FakeProgram();
        var declaringType = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "Roots", "Box`1", isValueType: false),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var constructor = Instance(
            Method(ConstructorKey, isStatic: false, name: ".ctor",
                parameters: [CliValueKind.I4]),
            declaringType);
        var allocator = Instance(Method(AllocatorKey), declaringType);
        var caller = Instance(Method(EntryKey), declaringType);
        var leaf = Instance(Method(LeafKey), declaringType);

        var allocatorBody = StructuredInstanceWithLocals(
            program,
            allocator,
            [CliValueKind.ManagedReference],
            1,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Return));
        var callerBody = StructuredInstance(
            program,
            caller,
            I(0, CilOperation.Call, new CilOperand.MethodInstance(allocator)),
            I(1, CilOperation.Return));
        var leafBody = StructuredInstance(
            program,
            leaf,
            I(0, CilOperation.Return));
        var methods = new Dictionary<string, StructuredMethod>
        {
            [allocator.CanonicalName] = allocatorBody,
            [caller.CanonicalName] = callerBody,
            [leaf.CanonicalName] = leafBody,
        };

        var allocationAnalyzer = CreateAllocationAnalyzer(program);
        var allocating = allocationAnalyzer.Analyze(
            new AllocationCapabilityAnalysisRequest(
                ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
                methods,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(allocator.CanonicalName, allocating.ConstructedMethods);
        Assert.Contains(caller.CanonicalName, allocating.ConstructedMethods);
        Assert.DoesNotContain(leaf.CanonicalName, allocating.ConstructedMethods);
        Assert.Throws<ArgumentNullException>(() =>
            allocationAnalyzer.Analyze(
                new AllocationCapabilityAnalysisRequest(
                    ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
                    null!,
                    ImmutableDictionary<string, DispatchCallSiteModel>.Empty)));

        var map = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            allocatorBody,
            new HashSet<EntityKey>(),
            new HashSet<string> { constructor.CanonicalName }));
        var safepoint = Assert.Single(map.Safepoints).Value;
        Assert.Empty(safepoint.Roots);
        Assert.Equal(
            new RootSource(RootSourceKind.AllocationTemporary, 0),
            Assert.Single(safepoint.ConstructorCallRoots));
        Assert.Throws<ArgumentNullException>(() =>
            CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
                allocatorBody,
                new HashSet<EntityKey>(),
                null!)));
    }

    [Fact]
    public void ConstructedStaticInitializationIsAnAllocatingSafepoint()
    {
        var program = new FakeProgram();
        var genericType = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "Roots", "Cache`1", isValueType: false),
            [CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference,
                isValueType: false)]);
        var field = new FieldInstanceModel(
            program.GetField(StaticFieldKey),
            genericType,
            CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference,
                isValueType: false));
        var initializer = Instance(program.GetMethod(StaticInitializerKey), genericType);
        var caller = Instance(program.GetMethod(EntryKey), genericType);
        var initializerBody = StructuredInstanceWithLocals(
            program,
            initializer,
            [CliValueKind.ManagedReference],
            1,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Return));
        var callerBody = StructuredInstanceWithLocals(
            program,
            caller,
            [CliValueKind.ManagedReference],
            1,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadStaticField, new CilOperand.FieldInstance(field)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(5, CilOperation.Pop),
            I(6, CilOperation.Return));
        var constructedMethods = new Dictionary<string, StructuredMethod>
        {
            [initializer.CanonicalName] = initializerBody,
            [caller.CanonicalName] = callerBody,
        };

        var allocationAnalyzer = CreateAllocationAnalyzer(program);
        var capabilities = allocationAnalyzer.Analyze(
            new AllocationCapabilityAnalysisRequest(
                ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
                constructedMethods,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));
        var map = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            callerBody,
            capabilities.Methods,
            capabilities.ConstructedMethods));

        Assert.Contains(initializer.CanonicalName, capabilities.ConstructedMethods);
        Assert.Contains(caller.CanonicalName, capabilities.ConstructedMethods);
        Assert.Contains(
            new RootSource(RootSourceKind.Local, 0),
            map.Safepoints[2].Roots);
    }

    [Fact]
    public void RootMapUsesLivenessStackTypesAndAllocationTemporary()
    {
        var program = new FakeProgram();
        var method = Method(
            EntryKey,
            parameters: [CliValueKind.ManagedReference, CliValueKind.I4]);
        var constructor = Instance(
            program.GetMethod(ConstructorKey),
            CliTypeIdentity.Named(
                Assembly,
                "Roots",
                "Object",
                isValueType: false));
        var structured = StructuredWithLocals(
            program,
            method,
            [
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
            ],
            1,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(3, CilOperation.StoreLocal, new CilOperand.Index(2)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(5, CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
            I(6, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(7, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(8, CilOperation.Call, new CilOperand.Entity(AllocatorKey)),
            I(9, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(10, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(13)),
            I(11, CilOperation.LoadLocal, new CilOperand.Index(2)),
            I(12, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(15)),
            I(13, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(14, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(16)),
            I(15, CilOperation.Return),
            I(16, CilOperation.Return));
        var analyzer = CreateAnalyzer(program);

        var map = analyzer.Analyze(new RootMapAnalysisRequest(
            structured,
            new HashSet<EntityKey> { AllocatorKey, ConstructorKey },
            new HashSet<string>()));

        var allocation = map.Safepoints[5];
        Assert.True(allocation.Roots.AsSpan().SequenceEqual(
            [new RootSource(RootSourceKind.Local, 0)]));
        Assert.True(allocation.ConstructorCallRoots.AsSpan().SequenceEqual(
            [
                new RootSource(RootSourceKind.Local, 0),
                new RootSource(RootSourceKind.AllocationTemporary, 0),
            ]));

        var call = map.Safepoints[8];
        Assert.True(call.Roots.AsSpan().SequenceEqual(
            [
                new RootSource(RootSourceKind.Local, 1),
                new RootSource(RootSourceKind.EvaluationStack, 0),
            ]));
        Assert.Empty(call.ConstructorCallRoots);
        Assert.Equal(4, map.SlotCount);
        Assert.Equal(EntryKey, map.Method);
        Assert.Equal(0, map.Slots[new RootSource(RootSourceKind.Local, 0)]);
        Assert.Equal(1, map.Slots[new RootSource(RootSourceKind.Local, 1)]);
        Assert.Equal(2, map.Slots[new RootSource(RootSourceKind.EvaluationStack, 0)]);
        Assert.Equal(3, map.Slots[new RootSource(RootSourceKind.AllocationTemporary, 0)]);
    }

    [Fact]
    public void ManagedAddressArgumentPointeeRemainsRootedAfterLastExplicitUse()
    {
        var program = new FakeProgram();
        var reference = CliTypeIdentity.Primitive(
            "object", CliValueKind.ManagedReference, isValueType: false);
        var method = Method(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                CliTypeIdentity.ManagedByReference(reference)),
        };
        var structured = StructuredWithLocals(
            program,
            method,
            [],
            2,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.LoadNull),
            I(2, CilOperation.StoreObject, new CilOperand.TypeIdentity(reference)),
            I(3, CilOperation.LoadNull),
            I(4, CilOperation.Call, new CilOperand.Entity(AllocatorKey)),
            I(5, CilOperation.Return));

        var map = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            structured,
            new HashSet<EntityKey> { AllocatorKey },
            new HashSet<string>()));

        var roots = map.Safepoints[4].Roots;
        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressArgument, 0),
            roots);
        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressArgument, 0, 0),
            roots);
    }

    [Fact]
    public void RootMapOmitsMethodsWithoutSafepointsAndValidatesArguments()
    {
        var program = new FakeProgram();
        var analyzer = CreateAnalyzer(program);
        var leaf = Structured(program, Method(LeafKey), I(0, CilOperation.Return));

        var result = analyzer.Analyze(new RootMapAnalysisRequest(
            leaf,
            new HashSet<EntityKey>(),
            new HashSet<string>()));

        Assert.Empty(result.Safepoints);
        Assert.Empty(result.Slots);
        Assert.Throws<ArgumentNullException>(() =>
            new RootMapAnalyzer(null!, program, EmptyValueLayouts.Instance, CreateRootDecisions(program)));
        Assert.Throws<ArgumentNullException>(() =>
            new RootMapAnalyzer(program, null!, EmptyValueLayouts.Instance, CreateRootDecisions(program)));
        Assert.Throws<ArgumentNullException>(() =>
            new RootMapAnalyzer(program, program, null!, CreateRootDecisions(program)));
        Assert.Throws<ArgumentNullException>(() =>
            new RootMapAnalyzer(program, program, EmptyValueLayouts.Instance, null!));
        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(null!));
        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(
            new RootMapAnalysisRequest(
                null!,
                new HashSet<EntityKey>(),
                new HashSet<string>())));
        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(
            new RootMapAnalysisRequest(leaf, null!, new HashSet<string>())));
        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(
            new RootMapAnalysisRequest(leaf, new HashSet<EntityKey>(), null!)));
    }

    [Fact]
    public void RootDecisionClassifierProvidesOneDirectDecisionContract()
    {
        var program = new FakeProgram();
        var method = Structured(program, Method(LeafKey), I(0, CilOperation.Return));
        var block = Assert.Single(method.ControlFlow.Graph.Blocks);
#pragma warning disable CA1859 // Contract test deliberately dispatches through capability interfaces.
        IRootDecisionClassifier classifier = new RootDecisionClassifier(
            program,
            program,
            program);
        IRootDecisionClassifierFactory factory = new RootDecisionClassifierFactory();
#pragma warning restore CA1859

        Assert.True(classifier.Decide(new(
            method,
            block.Index,
            I(0, CilOperation.CallIndirect),
            new HashSet<EntityKey>(),
            new HashSet<string>())));
        Assert.False(classifier.Decide(new(
            method,
            block.Index,
            I(0, CilOperation.Nop),
            new HashSet<EntityKey>(),
            new HashSet<string>())));
        Assert.False(classifier.Decide(new(
            method,
            block.Index,
            I(0, CilOperation.Call),
            new HashSet<EntityKey>(),
            new HashSet<string>())));
        Assert.False(classifier.Decide(new(
            method,
            block.Index,
            I(0, CilOperation.LoadStaticField),
            new HashSet<EntityKey>(),
            new HashSet<string>())));
        Assert.True(classifier.Decide(new(
            method,
            block.Index,
            I(0, CilOperation.Call, new CilOperand.Entity(JSImportKey)),
            new HashSet<EntityKey>(),
            new HashSet<string>())));
        Assert.IsAssignableFrom<IRootDecisionClassifier>(factory.Create(
            program,
            program,
            program,
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty));
        Assert.Throws<ArgumentNullException>(() => classifier.Decide(null!));
        Assert.Throws<ArgumentNullException>(() => classifier.Decide(new(
            null!, block.Index, I(0, CilOperation.Nop),
            new HashSet<EntityKey>(), new HashSet<string>())));
        Assert.Throws<ArgumentNullException>(() => classifier.Decide(new(
            method, block.Index, null!, new HashSet<EntityKey>(), new HashSet<string>())));
        Assert.Throws<ArgumentNullException>(() => classifier.Decide(new(
            method, block.Index, I(0, CilOperation.Nop), null!, new HashSet<string>())));
        Assert.Throws<ArgumentNullException>(() => classifier.Decide(new(
            method, block.Index, I(0, CilOperation.Nop), new HashSet<EntityKey>(), null!)));
        Assert.Throws<ArgumentNullException>(() =>
            new RootDecisionClassifier(null!, program, program));
        Assert.Throws<ArgumentNullException>(() =>
            new RootDecisionClassifier(program, null!, program));
        Assert.Throws<ArgumentNullException>(() =>
            new RootDecisionClassifier(program, program, null!));
    }

    [Fact]
    public void RootMapPublishesManagedAddressesAcrossAllocatingCalls()
    {
        var program = new FakeProgram();
        var method = Method(EntryKey, parameters: [CliValueKind.ManagedAddress]);
        var structured = StructuredWithLocals(
            program,
            method,
            [CliValueKind.ManagedAddress],
            1,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadNull),
            I(3, CilOperation.Call, new CilOperand.Entity(AllocatorKey)),
            I(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(5, CilOperation.Pop),
            I(6, CilOperation.Return));

        var map = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            structured,
            new HashSet<EntityKey> { AllocatorKey },
            new HashSet<string>()));

        var safepoint = map.Safepoints[3];
        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressLocal, 0),
            safepoint.Roots);
        Assert.Contains(
            new RootSource(RootSourceKind.EvaluationStack, 0),
            safepoint.Roots);
        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressArgument, 0),
            safepoint.Roots);
        Assert.Equal(3, map.SlotCount);
    }

    [Fact]
    public void RootMapPublishesReferenceStoredThroughManagedAddress()
    {
        var program = new FakeProgram();
        var reference = CliTypeIdentity.Primitive(
            "object", CliValueKind.ManagedReference, isValueType: false);
        var byReference = CliTypeIdentity.ManagedByReference(reference);
        var definition = Method(EntryKey, parameters: [CliValueKind.ManagedAddress]) with
        {
            Signature = new MethodSignatureModel(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                [byReference]),
        };
        CilMethodBody body = new(
            definition,
            1,
            [],
            [
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.Call, new CilOperand.Entity(AllocatorKey)),
                I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Return),
            ]);
        var graph = CreateGraphBuilder().Build(body);
        var structured = CreateStructuredMethod(
            new TypedStackValidator(
                program,
                program,
                program,
                new StackTypeCompatibilityValidator()).Validate(graph));

        var map = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            structured,
            new HashSet<EntityKey> { AllocatorKey },
            new HashSet<string>()));

        var roots = map.Safepoints[1].Roots;
        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressArgument, 0),
            roots);
        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressArgument, 0, 0),
            roots);
    }

    [Fact]
    public void RootMapPublishesReferencesInsideValueStoredThroughManagedAddress()
    {
        var program = new FakeProgram();
        var definition = Method(EntryKey, parameters: [CliValueKind.ManagedAddress]) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                CliTypeIdentity.ManagedByReference(PairType)),
        };
        CilMethodBody body = new(
            definition,
            1,
            [],
            [
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.Call, new CilOperand.Entity(AllocatorKey)),
                I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Return),
            ]);
        var graph = CreateGraphBuilder().Build(body);
        var structured = CreateStructuredMethod(
            new TypedStackValidator(
                program,
                program,
                program,
                new StackTypeCompatibilityValidator()).Validate(graph));

        var map = CreateAnalyzer(program, new FakeLayouts(PairType)).Analyze(
            new RootMapAnalysisRequest(
                structured,
                new HashSet<EntityKey> { AllocatorKey },
                new HashSet<string>()));

        var roots = map.Safepoints[1].Roots;
        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressArgument, 0),
            roots);
        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressArgument, 0, 4),
            roots);
    }

    [Fact]
    public void RootMapDoesNotFrameANonAllocatingCallThatCannotResumeLocally()
    {
        var program = new FakeProgram();
        var analyzer = CreateAnalyzer(program);
        var caller = Structured(
            program,
            Method(LeafKey),
            I(0, CilOperation.Nop, new CilOperand.Index(0)),
            I(1, CilOperation.Call, new CilOperand.Entity(ExternalKey)),
            I(2, CilOperation.Return));

        Assert.Empty(analyzer.Analyze(new RootMapAnalysisRequest(
            caller,
            new HashSet<EntityKey>(),
            new HashSet<string>())).Safepoints);
        Assert.Empty(analyzer.Analyze(new RootMapAnalysisRequest(
            caller,
            new HashSet<EntityKey> { ExternalKey },
            new HashSet<string>())).Safepoints);
    }

    [Fact]
    public void RootMapIncludesReferencesUsedOnlyByAnExceptionalSuccessor()
    {
        var program = new FakeProgram();
        var method = Method(
            EntryKey,
            parameters: [CliValueKind.ManagedReference]);
        var body = new CilMethodBody(
            method,
            1,
            [CliValueKind.ManagedReference],
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(2, CilOperation.Call, new CilOperand.Entity(LeafKey)),
                I(3, CilOperation.Leave, new CilOperand.BranchTarget(8)),
                I(4, CilOperation.Pop),
                I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(6, CilOperation.Pop),
                I(7, CilOperation.Leave, new CilOperand.BranchTarget(8)),
                I(8, CilOperation.Return),
            ])
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    2,
                    2,
                    4,
                    4,
                    TypeKey,
                    null),
            ],
        };
        var structured = CreateStructuredMethod(
            new TypedStackValidator(
                program,
                program,
                program,
                new StackTypeCompatibilityValidator()).Validate(
                CreateGraphBuilder().Build(body)));

        var roots = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            structured,
            new HashSet<EntityKey>(),
            new HashSet<string>()));

        Assert.Contains(
            new RootSource(RootSourceKind.Local, 0),
            roots.Safepoints[2].Roots);
    }

    [Fact]
    public void RootMapIncludesFilterSuccessorsWhenCallsMayThrow()
    {
        var program = new FakeProgram();
        var method = Method(EntryKey, parameters: [CliValueKind.ManagedReference]);
        var body = new CilMethodBody(
            method,
            2,
            [CliValueKind.ManagedReference],
            [
                I(0, CilOperation.Call, new CilOperand.Entity(LeafKey)),
                I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(3, CilOperation.Call, new CilOperand.Entity(LeafKey)),
                I(4, CilOperation.Leave, new CilOperand.BranchTarget(12)),
                I(5, CilOperation.Pop),
                I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(7, CilOperation.EndFilter),
                I(8, CilOperation.Pop),
                I(9, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(10, CilOperation.Pop),
                I(11, CilOperation.Leave, new CilOperand.BranchTarget(12)),
                I(12, CilOperation.Return),
            ])
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Filter,
                    3,
                    2,
                    8,
                    4,
                    null,
                    5),
            ],
        };
        var structured = CreateStructuredMethod(
            new TypedStackValidator(
                program,
                program,
                program,
                new StackTypeCompatibilityValidator()).Validate(
                CreateGraphBuilder().Build(body)));

        var roots = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            structured,
            new HashSet<EntityKey>(),
            new HashSet<string>()));

        Assert.Contains(3, roots.Safepoints.Keys);
    }

    [Fact]
    public void RootMapPublishesReferencesEmbeddedInValueArgumentsAndBoxOperands()
    {
        var program = new FakeProgram();
        var pair = CliTypeIdentity.Named(
            Assembly, "Roots", "Pair", isValueType: true);
        var definition = Method(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void), pair),
        };
        var instance = Instance(
            definition,
            CliTypeIdentity.Named(Assembly, "Roots", "Object", isValueType: false));
        var body = new CilMethodBody(
            definition,
            1,
            [],
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.Box, new CilOperand.TypeIdentity(pair)),
                I(2, CilOperation.Pop),
                I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(4, CilOperation.Pop),
                I(5, CilOperation.Return),
            ])
        {
            MethodInstance = instance,
            LocalSignatureTypes = [],
        };
        var structured = CreateStructuredMethod(
            new TypedStackValidator(
                program,
                program,
                program,
                new StackTypeCompatibilityValidator()).Validate(
                CreateGraphBuilder().Build(body)));

        var rootMapAnalyzer = CreateAnalyzer(program, new FakeLayouts(pair));
        var map = rootMapAnalyzer.Analyze(new RootMapAnalysisRequest(
                structured,
                new HashSet<EntityKey>(),
                new HashSet<string>()));

        var safepoint = map.Safepoints[1];
        Assert.Contains(
            new RootSource(RootSourceKind.Argument, 0, 4), safepoint.Roots);
        Assert.Contains(
            new RootSource(RootSourceKind.EvaluationStack, 0, 4), safepoint.Roots);
    }

    [Fact]
    public void AllocationCapabilityCoversDirectStaticFieldsIndirectCallsAndLeafInstances()
    {
        var program = new FakeProgram();
        var declaringType = CliTypeIdentity.Named(
            Assembly, "Roots", "Cache", isValueType: false);
        var field = new FieldInstanceModel(
            program.GetField(StaticFieldKey),
            declaringType,
            CliTypeIdentity.Primitive(
                "object", CliValueKind.ManagedReference, isValueType: false));
        var constructor = Instance(
            Method(ConstructorKey, isStatic: false, name: ".ctor",
                parameters: [CliValueKind.I4]),
            declaringType);
        var leaf = Instance(Method(LeafKey), declaringType);
        var initializer = StructuredWithLocals(
            program,
            program.GetMethod(StaticInitializerKey),
            [],
            2,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));
        var caller = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.LoadStaticField, new CilOperand.FieldInstance(field)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.LoadStaticField, new CilOperand.Entity(UninitializedStaticFieldKey)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadStaticField, new CilOperand.Entity(StaticFieldKey)),
            I(5, CilOperation.Pop),
            I(6, CilOperation.LoadFunction, new CilOperand.Entity(LeafKey)),
            I(7, CilOperation.CallIndirect, new CilOperand.CallSite(
                MethodSignatureModel.Create(CliValueKind.Void))),
            I(8, CilOperation.Call, new CilOperand.MethodInstance(leaf)),
            I(9, CilOperation.Return));
        var staticFieldCaller = Structured(
            program,
            Method(ExternalKey),
            I(0, CilOperation.LoadStaticField, new CilOperand.Entity(UninitializedStaticFieldKey)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.LoadStaticField, new CilOperand.Entity(StaticFieldKey)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.Return));

        var capabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                new Dictionary<EntityKey, StructuredMethod>
                {
                    [StaticInitializerKey] = initializer,
                    [EntryKey] = caller,
                    [ExternalKey] = staticFieldCaller,
                },
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(StaticInitializerKey, capabilities.Methods);
        Assert.Contains(EntryKey, capabilities.Methods);
    }

    [Fact]
    public void AllocationCapabilityUsesDefinitionIdentityForUninstantiatedConstructedMethods()
    {
        var program = new FakeProgram();
        var initializer = StructuredWithLocals(
            program,
            program.GetMethod(StaticInitializerKey),
            [],
            2,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));
        const string identity = "constructed-definition";

        var capabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
                new Dictionary<string, StructuredMethod> { [identity] = initializer },
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(identity, capabilities.ConstructedMethods);
    }

    [Fact]
    public void AllocationCapabilityCoversEntityMethodDispatchAndUninitializedFieldContracts()
    {
        var program = new FakeProgram();
        var leaf = Instance(Method(LeafKey),
            CliTypeIdentity.Named(Assembly, "Roots", "Object", isValueType: false));
        var field = new FieldInstanceModel(
            program.GetField(StaticFieldKey),
            CliTypeIdentity.Named(Assembly, "Roots", "Cache", isValueType: false),
            CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, isValueType: false));

        var fieldOnly = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.LoadStaticField, new CilOperand.FieldInstance(field)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.LoadStaticField, new CilOperand.Entity(StaticFieldKey)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.Call, new CilOperand.Entity(LeafKey)),
            I(5, CilOperation.Return));
        var fieldOnlyCapabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = fieldOnly },
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));
        Assert.DoesNotContain(EntryKey, fieldOnlyCapabilities.Methods);

        var imported = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.Call, new CilOperand.Entity(JSImportKey)),
            I(1, CilOperation.Return));
        var importedCapabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = imported },
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));
        Assert.Contains(EntryKey, importedCapabilities.Methods);

        var instanceCall = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.Call, new CilOperand.MethodInstance(leaf)),
            I(1, CilOperation.Return));
        var instanceCapabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = instanceCall },
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));
        Assert.DoesNotContain(EntryKey, instanceCapabilities.Methods);

        var importedInstance = Instance(
            program.GetMethod(JSImportKey),
            CliTypeIdentity.Named(Assembly, "Roots", "Object", isValueType: false));
        var importedInstanceBody = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.Call, new CilOperand.MethodInstance(importedInstance)),
            I(1, CilOperation.Return));
        var importedInstanceCapabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = importedInstanceBody },
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));
        Assert.Contains(EntryKey, importedInstanceCapabilities.Methods);

        var allocator = Instance(Method(AllocatorKey),
            CliTypeIdentity.Named(Assembly, "Roots", "Object", isValueType: false));
        var allocatorBody = StructuredInstance(
            program,
            allocator,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));
        var dispatchCaller = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.Call, new CilOperand.Entity(LeafKey)),
            I(1, CilOperation.Call, new CilOperand.Entity(LeafKey)),
            I(2, CilOperation.Return));
        var dispatchLeaf = Instance(Method(LeafKey), allocator.DeclaringType);
        var dispatchCallerIdentity = dispatchCaller.Method.CanonicalName;
        var dispatchSites = new Dictionary<string, DispatchCallSiteModel>
        {
            [$"{dispatchCallerIdentity}@00000000"] = new(
                dispatchCallerIdentity, 0, dispatchLeaf,
                [new DispatchTargetModel(allocator.DeclaringType, allocator)]),
            [$"{dispatchCallerIdentity}@00000001"] = new(
                dispatchCallerIdentity, 1, dispatchLeaf, []),
        };
        var dispatchCapabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = dispatchCaller },
                new Dictionary<string, StructuredMethod>
                {
                    [allocator.CanonicalName] = allocatorBody,
                },
                dispatchSites));
        Assert.Contains(EntryKey, dispatchCapabilities.Methods);
    }

    [Fact]
    public void AllocationCapabilityUsesCanonicalIdentityForDirectMethodInstances()
    {
        var program = new FakeProgram();
        var method = Instance(Method(EntryKey),
            CliTypeIdentity.Named(Assembly, "Roots", "Object", isValueType: false));
        var body = StructuredInstance(
            program,
            method,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));

        var capabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = body },
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(EntryKey, capabilities.Methods);
    }

    [Fact]
    public void AllocationCapabilityRecognizesAStaticFieldInitializerWithoutOtherAllocatingCalls()
    {
        var program = new FakeProgram();
        var field = new FieldInstanceModel(
            program.GetField(StaticFieldKey),
            CliTypeIdentity.Named(Assembly, "Roots", "Cache", isValueType: false),
            CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, isValueType: false));
        var initializer = Structured(
            program,
            new MethodDefinitionModel(
                StaticInitializerKey,
                GenericTypeKey,
                ".cctor",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));
        var caller = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.LoadStaticField, new CilOperand.FieldInstance(field)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Return));

        var capabilities = CreateAllocationAnalyzer(program).Analyze(
            new AllocationCapabilityAnalysisRequest(
                new Dictionary<EntityKey, StructuredMethod>
                {
                    [StaticInitializerKey] = initializer,
                    [EntryKey] = caller,
                },
                ImmutableDictionary<string, StructuredMethod>.Empty,
                ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(EntryKey, capabilities.Methods);
    }

    [Fact]
    public void RootMapCoversUninstantiatedInstanceSignaturesAndNonAllocatingConstructors()
    {
        var program = new FakeProgram();
        var instanceMethod = Method(
            LeafKey,
            isStatic: false,
            parameters: [CliValueKind.ManagedReference]);
        var instanceBody = Structured(
            program,
            instanceMethod,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Return));
        var constructorType = CliTypeIdentity.Named(
            Assembly, "Roots", "Value", isValueType: false);
        var constructor = Instance(
            Method(ConstructorKey, isStatic: false, name: ".ctor",
                parameters: [CliValueKind.I4]),
            constructorType);
        var allocationBody = StructuredWithLocals(
            program,
            Method(EntryKey),
            [],
            2,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));

        var map = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            allocationBody,
            new HashSet<EntityKey>(),
            new HashSet<string>()));

        Assert.Empty(map.Safepoints[1].ConstructorCallRoots);
        var entityAllocationBody = StructuredWithLocals(
            program,
            Method(EntryKey),
            [],
            2,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));
        var entityMap = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            entityAllocationBody,
            new HashSet<EntityKey>(),
            new HashSet<string>()));

        Assert.Empty(entityMap.Safepoints[1].ConstructorCallRoots);
        Assert.Empty(CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            instanceBody,
            new HashSet<EntityKey>(),
            new HashSet<string>())).Safepoints);
    }

    [Fact]
    public void RootMapEvaluatesManagedAddressElementKinds()
    {
        var program = new FakeProgram();
        var value = CliTypeIdentity.Named(Assembly, "Roots", "Value", isValueType: true);
        var reference = CliTypeIdentity.Primitive(
            "object", CliValueKind.ManagedReference, isValueType: false);
        foreach (var element in new[] { value, reference })
        {
            var signature = MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                CliTypeIdentity.ManagedByReference(element));
            var method = Method(EntryKey) with { Signature = signature };
            var body = Structured(
                program,
                method,
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.Pop),
                I(2, CilOperation.Return));

            Assert.Empty(CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
                body,
                new HashSet<EntityKey>(),
                new HashSet<string>())).Safepoints);
        }
    }

    [Fact]
    public void RootMapEmbedsValueArgumentsAndValueTypeReceiversAtCalls()
    {
        var program = new FakeProgram();
        var method = Method(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void), PairType),
        };
        var body = Structured(
            program,
            method,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Call, new CilOperand.Entity(PairCallKey)),
            I(2, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
            I(3, CilOperation.CallVirtual, new CilOperand.MethodInstance(
                Instance(
                    Method(PairVirtualKey, isStatic: false),
                    PairType))),
            I(4, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
            I(5, CilOperation.CallVirtual, new CilOperand.Entity(PairVirtualKey)),
            I(6, CilOperation.LoadNull),
            I(7, CilOperation.CallVirtual, new CilOperand.Entity(ReferenceVirtualKey)),
            I(8, CilOperation.Return));

        var map = CreateAnalyzer(program, new FakeLayouts(PairType)).Analyze(
            new RootMapAnalysisRequest(
                body,
                new HashSet<EntityKey> { PairCallKey, PairVirtualKey, ReferenceVirtualKey },
                new HashSet<string>()));

        Assert.Contains(
            new RootSource(RootSourceKind.EvaluationStack, 0, 4),
            map.Safepoints[1].Roots);
        Assert.Contains(
            new RootSource(RootSourceKind.EvaluationStack, 0, 4),
            map.Safepoints[3].Roots);
    }

    [Fact]
    public void RootMapFramesManagedAddressValuesAlreadyOnTheEvaluationStack()
    {
        var program = new FakeProgram();
        var method = Method(EntryKey, parameters: [CliValueKind.ManagedAddress]);
        var body = Structured(
            program,
            method,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Call, new CilOperand.Entity(AddressCallKey)),
            I(2, CilOperation.Return));

        var map = CreateAnalyzer(program, new FakeLayouts(PairType)).Analyze(
            new RootMapAnalysisRequest(
                body,
                new HashSet<EntityKey> { AddressCallKey },
                new HashSet<string>()));

        Assert.Contains(
            new RootSource(RootSourceKind.ManagedAddressEvaluationStack, 0),
            map.Safepoints[1].Roots);
        Assert.Contains(
            new RootSource(RootSourceKind.EvaluationStack, 0, 4),
            map.Safepoints[1].Roots);
    }

    [Fact]
    public void RootMapReadsConstructedInstanceParameterSignatures()
    {
        var program = new FakeProgram();
        var method = Instance(
            Method(LeafKey, isStatic: false, parameters: [CliValueKind.ManagedReference]),
            CliTypeIdentity.Named(Assembly, "Roots", "Object", isValueType: false));
        var body = StructuredInstance(
            program,
            method,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Return));

        Assert.Empty(CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            body,
            new HashSet<EntityKey>(),
            new HashSet<string>())).Safepoints);
    }

    [Fact]
    public void RootMapSkipsUnreachableBlocksWithNoDispatchTable()
    {
        var program = new FakeProgram();
        var body = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            I(1, CilOperation.Return),
            I(2, CilOperation.Return),
            I(3, CilOperation.Return));

        var map = new RootMapAnalyzer(
            program,
            program,
            EmptyValueLayouts.Instance,
            CreateRootDecisions(program)).Analyze(new RootMapAnalysisRequest(
                body,
                new HashSet<EntityKey>(),
                new HashSet<string>()));

        Assert.Empty(map.Safepoints);
    }

    [Fact]
    public void AllocationCapabilityUsesRuntimeSafepointsForEveryCallOperandShape()
    {
        var program = new FakeProgram();
        var classifier = new AlwaysRuntimeSafepointClassifier();
        var analyzer = new AllocationCapabilityAnalyzer(
            program,
            program,
            program,
            classifier);
        var declaringType = CliTypeIdentity.Named(
            Assembly,
            "Roots",
            "RuntimeBoundary",
            false);
        var directBody = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.Call, new CilOperand.Entity(LeafKey)),
            I(1, CilOperation.Return));
        var instanceBody = Structured(
            program,
            Method(EntryKey),
            I(
                0,
                CilOperation.Call,
                new CilOperand.MethodInstance(Instance(Method(LeafKey), declaringType))),
            I(1, CilOperation.Return));

        var direct = analyzer.Analyze(new AllocationCapabilityAnalysisRequest(
            new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = directBody },
            new Dictionary<string, StructuredMethod>(),
            new Dictionary<string, DispatchCallSiteModel>()));
        var constructed = analyzer.Analyze(new AllocationCapabilityAnalysisRequest(
            new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = instanceBody },
            new Dictionary<string, StructuredMethod>(),
            new Dictionary<string, DispatchCallSiteModel>()));

        Assert.Contains(EntryKey, direct.Methods);
        Assert.Contains(EntryKey, constructed.Methods);
        Assert.Equal(1, classifier.DefinitionCalls);
        Assert.Equal(1, classifier.InstanceCalls);
    }

    private static StructuredMethod Structured(
        FakeProgram program,
        MethodDefinitionModel method,
        CilInstruction first,
        params CilInstruction[] rest) =>
        StructuredWithLocals(program, method, [], 1, first, rest);

    private static StructuredMethod StructuredInstance(
        FakeProgram program,
        MethodInstanceModel method,
        CilInstruction first,
        params CilInstruction[] rest) =>
        StructuredInstanceWithLocals(program, method, [], 1, first, rest);

    private static StructuredMethod StructuredInstanceWithLocals(
        FakeProgram program,
        MethodInstanceModel method,
        ImmutableArray<CliValueKind> locals,
        int maxStack,
        CilInstruction first,
        params CilInstruction[] rest)
    {
        var body = new CilMethodBody(
            method.Definition, maxStack, locals, [first, .. rest])
        {
            MethodInstance = method,
        };
        var graph = CreateGraphBuilder().Build(body);
        var validated = new TypedStackValidator(
            program,
            program,
            program,
            new StackTypeCompatibilityValidator()).Validate(graph);
        return CreateStructuredMethod(validated);
    }

    private static StructuredMethod StructuredWithLocals(
        FakeProgram program,
        MethodDefinitionModel method,
        ImmutableArray<CliValueKind> locals,
        int maxStack,
        CilInstruction first,
        params CilInstruction[] rest)
    {
        CilMethodBody body = new(method, maxStack, locals, [first, .. rest])
        {
            MethodInstance = DirectInstance(program, method),
        };
        var graph = CreateGraphBuilder().Build(body);
        var validated = new TypedStackValidator(
            program,
            program,
            program,
            new StackTypeCompatibilityValidator()).Validate(graph);
        return CreateStructuredMethod(validated);
    }

    private static CilInstruction I(
        int offset,
        CilOperation operation,
        CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static MethodDefinitionModel Method(
        EntityKey key,
        bool isStatic = true,
        string name = "Method",
        ImmutableArray<CliValueKind> parameters = default,
        int rva = 1) => new(
        key,
        TypeKey,
        name,
        isStatic,
        new MethodSignatureModel(
            CliValueKind.Void,
            parameters.IsDefault ? [] : parameters),
        rva);

    private static EntityKey Key(int token) => new(Assembly, token);

    private static MethodInstanceModel Instance(
        MethodDefinitionModel method,
        CliTypeIdentity declaringType) => new(
        method,
        declaringType,
        [],
        method.Signature);

    private static IRootMapAnalyzer CreateAnalyzer(FakeProgram program)
        => CreateAnalyzer(program, EmptyValueLayouts.Instance);

    private static IControlFlowGraphBuilder CreateGraphBuilder() =>
        ((IControlFlowGraphBuilderFactory)new ControlFlowGraphBuilderFactory()).Create();

    private static StructuredMethod CreateStructuredMethod(
        ValidatedControlFlowGraph validated)
    {
        var body = validated.Graph.MethodBody;
        var instance = body.MethodInstance ?? new MethodInstanceModel(
            body.Method,
            CliTypeIdentity.Named(
                body.Method.DeclaringType.Assembly,
                "Roots",
                "Object",
                isValueType: false),
            [],
            body.Method.Signature);
        if (body.MethodInstance is null)
        {
            validated = validated with
            {
                Graph = CreateGraphBuilder().Build(body with { MethodInstance = instance }),
            };
        }
        return new(instance, validated);
    }

    private static MethodInstanceModel DirectInstance(
        FakeProgram program,
        MethodDefinitionModel method)
    {
        var type = program.GetTypeDefinition(method.DeclaringType);
        return new(
            method,
            CliTypeIdentity.Named(
                method.DeclaringType.Assembly,
                type.Namespace,
                type.Name,
                type.IsValueType),
            [],
            method.Signature);
    }

    private static IRootMapAnalyzer CreateAnalyzer(
        FakeProgram program,
        IValueLayoutProvider layouts)
    {
        var analyzers = new IRootMapAnalyzer[]
        {
            new RootMapAnalyzer(
                program,
                program,
                layouts,
                CreateRootDecisions(program)),
        };
        return analyzers[0];
    }

#pragma warning disable CA1859 // Composition helper returns the capability contract used by the analyzer.
    private static IRootDecisionClassifier CreateRootDecisions(FakeProgram program) =>
        new RootDecisionClassifier(program, program, program);
#pragma warning restore CA1859

    private static IAllocationCapabilityAnalyzer CreateAllocationAnalyzer(
        FakeProgram program)
    {
        var analyzers = new IAllocationCapabilityAnalyzer[]
        {
            new AllocationCapabilityAnalyzer(
                program,
                program,
                program,
                new RuntimeAllocationSafepointClassifier()),
        };
        return analyzers[0];
    }

    private sealed class EmptyValueLayouts : IValueLayoutProvider
    {
        public static EmptyValueLayouts Instance { get; } = new();

        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            new(type, sizeof(int), sizeof(int), []);
    }

    private sealed class AlwaysRuntimeSafepointClassifier :
        IRuntimeAllocationSafepointClassifier
    {
        public int DefinitionCalls { get; private set; }

        public int InstanceCalls { get; private set; }

        public bool Classify(MethodDefinitionModel method, ITypeRepository types)
        {
            ++DefinitionCalls;
            return true;
        }

        public bool Classify(MethodInstanceModel method)
        {
            ++InstanceCalls;
            return true;
        }
    }

    private sealed class FakeProgram :
        ITypeRepository,
        IFieldRepository,
        IMethodRepository,
        ISymbolFormatter,
        ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) => false;

        private readonly ImmutableDictionary<EntityKey, MethodDefinitionModel> _methods =
            new Dictionary<EntityKey, MethodDefinitionModel>
            {
                [EntryKey] = Method(EntryKey, parameters: [CliValueKind.ManagedReference]),
                [AllocatorKey] = Method(
                    AllocatorKey,
                    parameters: [CliValueKind.ManagedReference]),
                [ConstructorKey] = Method(
                    ConstructorKey,
                    isStatic: false,
                    name: ".ctor",
                    parameters: [CliValueKind.I4]),
                [LeafKey] = Method(LeafKey),
                [ExternalKey] = Method(ExternalKey, rva: 0),
                [JSImportKey] = Method(JSImportKey) with
                {
                    JSImport = new("run", "tests"),
                },
                [PairCallKey] = Method(PairCallKey) with
                {
                    Signature = MethodSignatureModel.Create(
                        CliTypeIdentity.Primitive("void", CliValueKind.Void), PairType),
                },
                [PairVirtualKey] = Method(PairVirtualKey, isStatic: false) with
                {
                    DeclaringType = ValueTypeKey,
                },
                [AddressCallKey] = Method(AddressCallKey) with
                {
                    Signature = MethodSignatureModel.Create(
                        CliTypeIdentity.Primitive("void", CliValueKind.Void),
                        CliTypeIdentity.ManagedByReference(PairType)),
                },
                [ReferenceVirtualKey] = Method(ReferenceVirtualKey, isStatic: false),
                [StaticInitializerKey] = new MethodDefinitionModel(
                    StaticInitializerKey,
                    GenericTypeKey,
                    ".cctor",
                    true,
                    MethodSignatureModel.Create(CliValueKind.Void),
                    1),
            }.ToImmutableDictionary();

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => key == GenericTypeKey
            ? new(key, "Roots", "Cache`1", false, [StaticFieldKey],
                [StaticInitializerKey])
            : key == ValueTypeKey
                ? new(key, "Roots", "Pair", true, [], [PairVirtualKey])
                : new(key, "Roots", "Object", false, [], [.. _methods.Keys]);

        public FieldDefinitionModel GetField(EntityKey key) => key == StaticFieldKey
            ? new(
                key,
                GenericTypeKey,
                "Value",
                CliTypeIdentity.GenericParameter(method: false, 0),
                true)
            : key == UninitializedStaticFieldKey
                ? new(
                    key,
                    TypeKey,
                    "Uninitialized",
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                    true)
            : throw new KeyNotFoundException();

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];

        public string Format(EntityKey key) => "Roots.Object";

        public string Format(MethodDefinitionModel method) =>
            $"Roots.Object::{method.Name}";
    }

    private sealed class FakeLayouts(CliTypeIdentity pair) : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            type == pair ? new(type, 8, 4, [4]) : new(type, 4, 4, []);
    }
}
