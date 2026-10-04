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
    private static readonly EntityKey NativeKey = Key(0x0600000c);
    private static readonly EntityKey StaticFieldKey = Key(0x04000001);
    private static readonly EntityKey UninitializedStaticFieldKey = Key(0x04000002);
    private static readonly EntityKey GenericTypeKey = Key(0x02000002);
    private static readonly EntityKey ValueTypeKey = Key(0x02000003);
    private static readonly EntityKey DelegateTypeKey = Key(0x02000004);
    private static readonly EntityKey EnumTypeKey = Key(0x02000005);
    private static readonly EntityKey DelegateInvokeKey = Key(0x0600000d);
    private static readonly CliTypeIdentity PairType =
        CliTypeIdentity.Named(Assembly, "Roots", "Pair", isValueType: true);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void DelegateCallsPublishRootsAndPropagateCollectionAcrossCallerKinds(
        bool instanceOperand, int dispatchShape)
    {
        var program = new FakeProgram(delegateType: true);
        var invoke = program.GetMethod(DelegateInvokeKey);
        Assert.False(invoke.HasBody);
        var owner = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "Roots", "CustomCallback`1", false),
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var invokeInstance = Instance(invoke, owner);
        var call = I(1, CilOperation.CallVirtual, instanceOperand
            ? new CilOperand.MethodInstance(invokeInstance)
            : new CilOperand.Entity(DelegateInvokeKey));
        var entry = Structured(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadNull), call, I(2, CilOperation.Return));
        var caller = Structured(program, program.GetMethod(AllocatorKey),
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, new CilOperand.Entity(EntryKey)),
            I(2, CilOperation.Return));
        var leaf = Structured(program, program.GetMethod(LeafKey), I(0, CilOperation.Return));
        var closedCaller = Instance(Method(Key(0x0600000e)), owner);
        var closedBody = StructuredInstance(program, closedCaller,
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, new CilOperand.Entity(AllocatorKey)),
            I(2, CilOperation.Return));
        var dispatch = new Dictionary<string, DispatchCallSiteModel>();
        if (dispatchShape != 0)
        {
            dispatch.Add(entry.Method.CanonicalName + "@00000001", new(
                entry.Method.CanonicalName, 1, invokeInstance,
                dispatchShape == 1 ? [] : [new(owner, leaf.Method)]));
        }

        var capabilities = CreateAllocationAnalyzer(program).Analyze(new(
            new Dictionary<EntityKey, StructuredMethod>
            {
                [EntryKey] = entry,
                [AllocatorKey] = caller,
                [LeafKey] = leaf,
            },
            new Dictionary<string, StructuredMethod> { [closedCaller.CanonicalName] = closedBody },
            dispatch));

        Assert.Contains(EntryKey, capabilities.Methods);
        Assert.Contains(AllocatorKey, capabilities.Methods);
        Assert.DoesNotContain(LeafKey, capabilities.Methods);
        Assert.DoesNotContain(DelegateInvokeKey, capabilities.Methods);
        Assert.Contains(closedCaller.CanonicalName, capabilities.ConstructedMethods);
        var decisions = ThroughContract(new RootDecisionClassifier(program, program, program,
            dispatch, new RuntimeAllocationSafepointClassifier(), program));
        Assert.True(decisions.Decide(new(entry, 0, call, new HashSet<EntityKey>(), new HashSet<string>())));
    }

    [Theory]
    [InlineData(false, false, "Invoke")]
    [InlineData(true, true, "Invoke")]
    [InlineData(true, false, "Other")]
    public void DelegateSafepointsExcludeOrdinaryNamesStaticMethodsAndOtherMembers(
        bool delegateType, bool isStatic, string name)
    {
        var program = new FakeProgram(delegateType: delegateType,
            delegateMethodName: name, delegateMethodStatic: isStatic);
        var call = I(1, CilOperation.Call, new CilOperand.Entity(DelegateInvokeKey));
        var body = Structured(program, program.GetMethod(EntryKey),
            isStatic ? I(0, CilOperation.Nop) : I(0, CilOperation.LoadNull),
            call, I(2, CilOperation.Return));

        var capabilities = CreateAllocationAnalyzer(program).Analyze(new(
            new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = body },
            ImmutableDictionary<string, StructuredMethod>.Empty,
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Empty(capabilities.Methods);
        Assert.Empty(capabilities.ConstructedMethods);
        Assert.False(CreateRootDecisions(program).Decide(new(
            body, 0, call, new HashSet<EntityKey>(), new HashSet<string>())));
    }

    private static IRootDecisionClassifier ThroughContract(IRootDecisionClassifier classifier) => classifier;

    [Theory]
    [InlineData("InternalGetValues", false)]
    [InlineData("InternalGetValues", true)]
    [InlineData("InternalGetValuesAsUnderlyingType", false)]
    [InlineData("InternalGetValuesAsUnderlyingType", true)]
    [InlineData("InternalGetNames", false)]
    [InlineData("InternalGetNames", true)]
    [InlineData("InternalToObject", false)]
    [InlineData("InternalToObject", true)]
    [InlineData("InternalGetUnderlyingType", false)]
    [InlineData("InternalGetUnderlyingType", true)]
    [InlineData("ToString", false)]
    [InlineData("ToString", true)]
    [InlineData("InternalToString", false)]
    [InlineData("InternalToString", true)]
    [InlineData("InternalFormat", false)]
    [InlineData("InternalFormat", true)]
    [InlineData("InternalToType", false)]
    [InlineData("InternalToType", true)]
    [InlineData("System.IConvertible.ToType", false)]
    [InlineData("System.IConvertible.ToType", true)]
    public void EnumIntrinsicsPublishLiveReferencesAndPropagateThroughManagedWrappers(
        string methodName, bool instanceOperand)
    {
        var program = new FakeProgram(enumIntrinsicName: methodName);
        var intrinsic = program.GetMethod(ExternalKey);
        Assert.False(intrinsic.HasBody);
        var intrinsicInstance = Instance(intrinsic,
            CliTypeIdentity.Named(Assembly, "System", "Enum", false));
        var call = I(0, CilOperation.Call, instanceOperand
            ? new CilOperand.MethodInstance(intrinsicInstance)
            : new CilOperand.Entity(ExternalKey));
        var wrapper = Structured(program, program.GetMethod(EntryKey),
            call, I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(2, CilOperation.Pop), I(3, CilOperation.Return));
        var caller = Structured(program, program.GetMethod(AllocatorKey),
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, new CilOperand.Entity(EntryKey)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.Pop), I(4, CilOperation.Return));
        var owner = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "Roots", "Caller`1", false),
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var closedCaller = Instance(program.GetMethod(AllocatorKey), owner);
        var closedBody = StructuredInstance(program, closedCaller,
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, new CilOperand.Entity(EntryKey)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.Pop), I(4, CilOperation.Return));
        var leaf = Structured(program, program.GetMethod(LeafKey), I(0, CilOperation.Return));
        var capabilities = CreateAllocationAnalyzer(program).Analyze(new(
            new Dictionary<EntityKey, StructuredMethod>
            {
                [EntryKey] = wrapper,
                [AllocatorKey] = caller,
                [LeafKey] = leaf,
            },
            new Dictionary<string, StructuredMethod> { [closedCaller.CanonicalName] = closedBody },
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(EntryKey, capabilities.Methods);
        Assert.Contains(AllocatorKey, capabilities.Methods);
        Assert.DoesNotContain(LeafKey, capabilities.Methods);
        Assert.Contains(closedCaller.CanonicalName, capabilities.ConstructedMethods);
        foreach (var (body, offset) in new[] { (wrapper, 0), (caller, 1), (closedBody, 1) })
        {
            var roots = CreateAnalyzer(program).Analyze(new(
                body, capabilities.Methods, capabilities.ConstructedMethods));
            Assert.Contains(new RootSource(RootSourceKind.Argument, 0), roots.Safepoints[offset].Roots);
        }
    }

    [Theory]
    [InlineData("ToString", false, 0, true)]
    [InlineData("ToString", false, 1, true)]
    [InlineData("ToString", false, 2, true)]
    [InlineData("ToString", true, 0, true)]
    [InlineData("ToString", true, 1, true)]
    [InlineData("ToString", true, 2, true)]
    [InlineData("System.IConvertible.ToType", false, 0, true)]
    [InlineData("System.IConvertible.ToType", false, 1, true)]
    [InlineData("System.IConvertible.ToType", false, 2, true)]
    [InlineData("System.IConvertible.ToType", true, 0, true)]
    [InlineData("System.IConvertible.ToType", true, 1, true)]
    [InlineData("System.IConvertible.ToType", true, 2, true)]
    [InlineData("InternalToInt32", false, 0, false)]
    [InlineData("InternalToInt32", true, 0, false)]
    public void IntrinsicAllocationPrecedesDispatchAndIncludesIntrinsicDispatchTargets(
        string methodName, bool instanceOperand, int dispatchShape, bool expected)
    {
        var program = new FakeProgram(enumIntrinsicName: methodName, enumIntrinsicStatic: false);
        var owner = CliTypeIdentity.Named(Assembly, "System", "Enum", false);
        var intrinsic = Instance(program.GetMethod(ExternalKey), owner);
        var declaration = dispatchShape == 0
            ? Instance(program.GetMethod(ReferenceVirtualKey),
                CliTypeIdentity.Named(Assembly, "System", "Object", false))
            : intrinsic;
        var call = I(1, CilOperation.CallVirtual, instanceOperand
            ? new CilOperand.MethodInstance(declaration)
            : new CilOperand.Entity(declaration.Definition.Key));
        var entry = Structured(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadNull), call,
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.Pop), I(4, CilOperation.Return));
        var caller = Structured(program, program.GetMethod(AllocatorKey),
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, new CilOperand.Entity(EntryKey)),
            I(2, CilOperation.Return));
        var leaf = Instance(program.GetMethod(ReferenceVirtualKey), declaration.DeclaringType);
        var dispatch = new Dictionary<string, DispatchCallSiteModel>
        {
            [entry.Method.CanonicalName + "@00000001"] = new(entry.Method.CanonicalName,
                1, declaration, dispatchShape switch
                {
                    0 => [new(owner, intrinsic)],
                    1 => [],
                    _ => [new(leaf.DeclaringType, leaf)],
                }),
        };
        var capabilities = CreateAllocationAnalyzer(program).Analyze(new(
            new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = entry, [AllocatorKey] = caller },
            ImmutableDictionary<string, StructuredMethod>.Empty, dispatch));
        var classifier = ThroughContract(new RootDecisionClassifier(program, program, program,
            dispatch, new RuntimeAllocationSafepointClassifier(), program));

        Assert.Equal(expected, capabilities.Methods.Contains(EntryKey));
        Assert.Equal(expected, capabilities.Methods.Contains(AllocatorKey));
        Assert.Equal(expected, classifier.Decide(new(entry, 0, call,
            capabilities.Methods, capabilities.ConstructedMethods)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCallsRequireRootsAndPropagateAllocationToManagedCallers(bool instanceOperand)
    {
        var program = new FakeProgram();
        var native = program.GetMethod(NativeKey);
        Assert.False(native.HasBody);
        var nativeInstance = Instance(native, CliTypeIdentity.Named(Assembly, "Roots", "Object", false));
        var call = I(0, CilOperation.Call, instanceOperand
            ? new CilOperand.MethodInstance(nativeInstance)
            : new CilOperand.Entity(NativeKey));
        var entry = Structured(program, Method(EntryKey), call, I(1, CilOperation.Return));
        var caller = Structured(program, Method(AllocatorKey),
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, new CilOperand.Entity(EntryKey)),
            I(2, CilOperation.Return));
        var leaf = Structured(program, Method(LeafKey), I(0, CilOperation.Return));
        var capabilities = CreateAllocationAnalyzer(program).Analyze(new(
            new Dictionary<EntityKey, StructuredMethod>
            {
                [EntryKey] = entry,
                [AllocatorKey] = caller,
                [LeafKey] = leaf,
            },
            ImmutableDictionary<string, StructuredMethod>.Empty,
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(EntryKey, capabilities.Methods);
        Assert.Contains(AllocatorKey, capabilities.Methods);
        Assert.DoesNotContain(LeafKey, capabilities.Methods);
        Assert.DoesNotContain(NativeKey, capabilities.Methods);
        Assert.True(CreateRootDecisions(program).Decide(new(
            entry, Assert.Single(entry.ControlFlow.Graph.Blocks).Index, call,
            new HashSet<EntityKey>(), new HashSet<string>())));
    }

    [Theory]
    [InlineData(CilOperation.NewRectangularArray)]
    [InlineData(CilOperation.NewBoundedRectangularArray)]
    public void RectangularAllocationCapabilityPropagatesToCallers(CilOperation operation)
    {
        var program = new FakeProgram();
        var array = CliTypeIdentity.Array(CliTypeIdentity.FromStackKind(CliValueKind.I4), 1);
        var instructions = new List<CilInstruction>();
        if (operation == CilOperation.NewBoundedRectangularArray)
            instructions.Add(I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(-2)));
        instructions.AddRange([
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(2, operation, new CilOperand.TypeIdentity(array)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.Return),
        ]);
        var allocator = StructuredWithLocals(program, Method(AllocatorKey), [], 2,
            instructions[0], [.. instructions.Skip(1)]);
        var caller = Structured(program, Method(EntryKey),
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, new CilOperand.Entity(AllocatorKey)),
            I(2, CilOperation.Return));

        var capabilities = CreateAllocationAnalyzer(program).Analyze(new(
            new Dictionary<EntityKey, StructuredMethod> { [AllocatorKey] = allocator, [EntryKey] = caller },
            ImmutableDictionary<string, StructuredMethod>.Empty,
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.Contains(AllocatorKey, capabilities.Methods);
        Assert.Contains(EntryKey, capabilities.Methods);
        var roots = CreateAnalyzer(program).Analyze(new(allocator, capabilities.Methods.ToHashSet(), new HashSet<string>()));
        Assert.True(roots.Safepoints.ContainsKey(2));
    }

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
                new RuntimeAllocationSafepointClassifier(), program));
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzer(
                program,
                null!,
                program,
                new RuntimeAllocationSafepointClassifier(), program));
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzer(
                program,
                program,
                null!,
                new RuntimeAllocationSafepointClassifier(), program));
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzer(program, program, program, null!, program));
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzer(program, program, program,
                new RuntimeAllocationSafepointClassifier(), null!));
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

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    public void StaticLeafCallsRootLiveLocalsAndArgumentsForTheirExactInitializer(
        int operandShape, bool allocates, bool differentClosedOwner)
    {
        var program = new FakeProgram(leafHasInitializer: true);
        var owner = CliTypeIdentity.Named(Assembly, "Roots", "Cache`1", false);
        if (operandShape == 2)
            owner = CliTypeIdentity.GenericInstantiation(owner,
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var initializerOwner = differentClosedOwner
            ? CliTypeIdentity.GenericInstantiation(owner.ElementType!,
                [CliTypeIdentity.Primitive("i8", CliValueKind.I8)])
            : owner;
        var leaf = Instance(program.GetMethod(LeafKey), owner);
        var initializer = Instance(program.GetMethod(StaticInitializerKey), initializerOwner);
        var initializerBody = allocates
            ? StructuredInstance(program, initializer,
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
                I(2, CilOperation.Pop), I(3, CilOperation.Return))
            : StructuredInstance(program, initializer, I(0, CilOperation.Return));
        var caller = StructuredWithLocals(program, program.GetMethod(EntryKey),
            [CliValueKind.ManagedReference], 1,
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Call, operandShape == 0
                ? new CilOperand.Entity(LeafKey) : new CilOperand.MethodInstance(leaf)),
            I(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(5, CilOperation.Pop), I(6, CilOperation.Return));
        var direct = new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = caller };
        var constructed = new Dictionary<string, StructuredMethod>();
        if (operandShape == 2)
            constructed.Add(initializer.CanonicalName, initializerBody);
        else
            direct.Add(StaticInitializerKey, initializerBody);

        var capabilities = CreateAllocationAnalyzer(program).Analyze(new(
            direct, constructed, ImmutableDictionary<string, DispatchCallSiteModel>.Empty));
        var map = CreateAnalyzer(program).Analyze(new(
            caller, capabilities.Methods, capabilities.ConstructedMethods));

        var expected = !differentClosedOwner;
        Assert.Equal(expected, capabilities.Methods.Contains(EntryKey));
        Assert.Equal(expected, map.Safepoints.ContainsKey(3));
        if (expected)
        {
            Assert.Contains(new RootSource(RootSourceKind.Local, 0), map.Safepoints[3].Roots);
            Assert.Contains(new RootSource(RootSourceKind.EvaluationStack, 0), map.Safepoints[3].Roots);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstanceCallsDoNotAcquireTheirOwnersInitializerDependency(bool methodInstance)
    {
        var program = new FakeProgram(leafHasInitializer: true, leafIsStatic: false);
        var owner = CliTypeIdentity.Named(Assembly, "Roots", "Cache`1", false);
        var leaf = Instance(program.GetMethod(LeafKey), owner);
        var initializer = Structured(program, program.GetMethod(StaticInitializerKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(2, CilOperation.Pop), I(3, CilOperation.Return));
        var caller = Structured(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, methodInstance
                ? new CilOperand.MethodInstance(leaf) : new CilOperand.Entity(LeafKey)),
            I(2, CilOperation.Return));
        var capabilities = CreateAllocationAnalyzer(program).Analyze(new(
            new Dictionary<EntityKey, StructuredMethod>
            {
                [EntryKey] = caller,
                [StaticInitializerKey] = initializer,
            }, ImmutableDictionary<string, StructuredMethod>.Empty,
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty));

        Assert.DoesNotContain(EntryKey, capabilities.Methods);
        Assert.Empty(CreateAnalyzer(program).Analyze(new(
            caller, capabilities.Methods, capabilities.ConstructedMethods)).Safepoints);
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
            program,
            null,
            new RuntimeAllocationSafepointClassifier(), program);
        IRootDecisionClassifierFactory factory = new RootDecisionClassifierFactory(
            new RuntimeAllocationSafepointClassifier());
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
            ImmutableDictionary<string, DispatchCallSiteModel>.Empty, program));
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
            new RootDecisionClassifier(
                null!, program, program, null,
                new RuntimeAllocationSafepointClassifier(), program));
        Assert.Throws<ArgumentNullException>(() =>
            new RootDecisionClassifier(
                program, null!, program, null,
                new RuntimeAllocationSafepointClassifier(), program));
        Assert.Throws<ArgumentNullException>(() =>
            new RootDecisionClassifier(
                program, program, null!, null,
                new RuntimeAllocationSafepointClassifier(), program));
        Assert.Throws<ArgumentNullException>(() =>
            new RootDecisionClassifier(program, program, program, null, null!, program));
        Assert.Throws<ArgumentNullException>(() =>
            new RootDecisionClassifier(program, program, program, null,
                new RuntimeAllocationSafepointClassifier(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new RootDecisionClassifierFactory(null!));
    }

    [Fact]
    public void RootDecisionClassifierUsesRuntimeSafepointsForEveryCallOperandShape()
    {
        var program = new FakeProgram();
        var runtimeSafepoints = new AlwaysRuntimeSafepointClassifier();
        var method = Structured(
            program,
            Method(EntryKey),
            I(0, CilOperation.Return));
        var block = Assert.Single(method.ControlFlow.Graph.Blocks);
        var classifier = new RootDecisionClassifier(
            program,
            program,
            program,
            null,
            runtimeSafepoints, program);

        Assert.True(classifier.Decide(new(
            method,
            block.Index,
            I(0, CilOperation.Call, new CilOperand.Entity(LeafKey)),
            new HashSet<EntityKey>(),
            new HashSet<string>())));
        Assert.True(classifier.Decide(new(
            method,
            block.Index,
            I(0, CilOperation.Call, new CilOperand.MethodInstance(
                Instance(program.GetMethod(LeafKey),
                    CliTypeIdentity.Named(Assembly, "Roots", "Object", false)))),
            new HashSet<EntityKey>(),
            new HashSet<string>())));
        Assert.Equal(1, runtimeSafepoints.DefinitionCalls);
        Assert.Equal(1, runtimeSafepoints.InstanceCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RootDecisionsPreserveDispatchAndDirectStaticFieldAllocationContracts(
        bool constructed, bool allocates)
    {
        var program = new FakeProgram();
        var caller = Structured(program, Method(EntryKey), I(0, CilOperation.Return));
        var owner = CliTypeIdentity.Named(Assembly, "Roots", "Object", false);
        if (constructed)
            owner = CliTypeIdentity.GenericInstantiation(owner,
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var target = Instance(program.GetMethod(LeafKey), owner);
        var key = caller.Method.CanonicalName + "@00000000";
        var dispatch = new Dictionary<string, DispatchCallSiteModel>
        {
            [key] = new(caller.Method.CanonicalName, 0, target,
                [new DispatchTargetModel(owner, target)]),
        };
        var classifier = Assert.IsAssignableFrom<IRootDecisionClassifier>(
            new RootDecisionClassifier(
                program,
                program,
                program,
                dispatch,
                new RuntimeAllocationSafepointClassifier(), program));
        var methods = new HashSet<EntityKey>();
        var instances = new HashSet<string>();
        if (allocates)
        {
            if (constructed) instances.Add(target.CanonicalName);
            else methods.Add(LeafKey);
            methods.Add(StaticInitializerKey);
        }

        Assert.Equal(allocates, classifier.Decide(new(caller, 0,
            I(0, CilOperation.Call, new CilOperand.MethodInstance(target)), methods, instances)));
        Assert.Equal(allocates, classifier.Decide(new(caller, 0,
            I(1, CilOperation.LoadStaticField, new CilOperand.Entity(StaticFieldKey)), methods, instances)));
        Assert.False(classifier.Decide(new(caller, 0,
            I(1, CilOperation.LoadStaticField, new CilOperand.Entity(UninitializedStaticFieldKey)), methods, instances)));
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    public void AccessorBodiesPublishRootsOnlyWhenTheirManagedCallCanAllocate(int shape, bool accessor, bool allocates)
    {
        var program = new FakeProgram(externalAccessor: accessor);
        var caller = Structured(program, Method(EntryKey), I(0, CilOperation.Return));
        var owner = CliTypeIdentity.Named(Assembly, "Roots", "Object", false);
        if (shape == 2)
            owner = CliTypeIdentity.GenericInstantiation(owner, [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var target = Instance(program.GetMethod(ExternalKey), owner);
        var methods = new HashSet<EntityKey>();
        var instances = new HashSet<string>();
        if (allocates)
        {
            if (shape == 2) instances.Add(target.CanonicalName);
            else methods.Add(ExternalKey);
        }
        CilOperand operand = shape == 0 ? new CilOperand.Entity(ExternalKey) : new CilOperand.MethodInstance(target);

        Assert.False(target.Definition.HasBody);
        Assert.Equal(accessor && allocates, CreateRootDecisions(program).Decide(new(caller, 0,
            I(0, CilOperation.Call, operand), methods, instances)));
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CompareExchangePublishesHandlerRootsOnlyWhenProtected(int handlerKind)
    {
        var program = new FakeProgram();
        var reference = CliTypeIdentity.Named(Assembly, "Roots", "Object", false);
        var handlerOffset = handlerKind == 2 ? 11 : 8;
        var returnOffset = handlerOffset + 4;
        var instructions = new List<CilInstruction>
        {
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(1)),
            I(3, CilOperation.LoadNull),
            I(4, CilOperation.LoadNull),
            I(5, CilOperation.CompareExchange, new CilOperand.TypeIdentity(reference)),
            I(6, CilOperation.Pop),
        };
        if (handlerKind == 0)
        {
            instructions.Add(I(7, CilOperation.Return));
        }
        else
        {
            instructions.Add(I(7, CilOperation.Leave, new CilOperand.BranchTarget(returnOffset)));
            if (handlerKind == 2)
            {
                instructions.AddRange([
                    I(8, CilOperation.Pop),
                    I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    I(10, CilOperation.EndFilter),
                ]);
            }
            instructions.AddRange([
                I(handlerOffset, CilOperation.Pop),
                I(handlerOffset + 1, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(handlerOffset + 2, CilOperation.Pop),
                I(handlerOffset + 3, CilOperation.Leave, new CilOperand.BranchTarget(returnOffset)),
                I(returnOffset, CilOperation.Return),
            ]);
        }
        var body = new CilMethodBody(
            Method(EntryKey, parameters: [CliValueKind.ManagedReference]),
            3,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [.. instructions])
        {
            ExceptionRegions = handlerKind == 0 ? [] :
            [
                new CilExceptionRegion(
                    handlerKind == 2 ? CilExceptionRegionKind.Filter : CilExceptionRegionKind.Catch,
                    2, 6, handlerOffset, 4,
                    handlerKind == 1 ? TypeKey : null,
                    handlerKind == 2 ? 8 : null),
            ],
        };
        var graph = CreateGraphBuilder().Build(body);
        var structured = CreateStructuredMethod(new TypedStackValidator(
            program, program, program, new StackTypeCompatibilityValidator()).Validate(graph));

        var roots = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            structured, new HashSet<EntityKey>(), new HashSet<string>()));

        if (handlerKind == 0)
        {
            Assert.Empty(roots.Safepoints);
        }
        else
        {
            Assert.Contains(new RootSource(RootSourceKind.Local, 0), roots.Safepoints[5].Roots);
            var protectedBlock = Assert.Single(graph.Blocks,
                block => block.Instructions.Any(instruction => instruction.Offset == 5));
            var successor = Assert.Single(graph.ExceptionalSuccessors[protectedBlock.Index]);
            Assert.Equal(8, graph.Blocks[successor].StartOffset);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FilterCollectionsRetainAcceptedSiblingAndOuterHandlerValues(int filterResult)
    {
        var program = new FakeProgram();
        var reference = CliTypeIdentity.Named(Assembly, "Roots", "Object", false);
        var body = new CilMethodBody(
            Method(EntryKey, parameters:
            [
                CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference, CliValueKind.ManagedReference,
            ]),
            3,
            [
                CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
            ],
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(2, CilOperation.LoadArgument, new CilOperand.Index(1)),
                I(3, CilOperation.StoreLocal, new CilOperand.Index(1)),
                I(4, CilOperation.LoadArgument, new CilOperand.Index(2)),
                I(5, CilOperation.StoreLocal, new CilOperand.Index(2)),
                I(6, CilOperation.LoadArgument, new CilOperand.Index(3)),
                I(7, CilOperation.StoreLocal, new CilOperand.Index(3)),
                I(8, CilOperation.LoadLocalAddress, new CilOperand.Index(4)),
                I(9, CilOperation.LoadNull),
                I(10, CilOperation.LoadNull),
                I(11, CilOperation.CompareExchange, new CilOperand.TypeIdentity(reference)),
                I(12, CilOperation.Pop),
                I(13, CilOperation.Leave, new CilOperand.BranchTarget(36)),
                I(14, CilOperation.Pop),
                I(15, CilOperation.Call, new CilOperand.Entity(LeafKey)),
                I(16, CilOperation.LoadInt32, new CilOperand.ConstantI4(filterResult)),
                I(17, CilOperation.EndFilter),
                I(18, CilOperation.Pop),
                I(19, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(20, CilOperation.Pop),
                I(21, CilOperation.Leave, new CilOperand.BranchTarget(36)),
                I(22, CilOperation.Pop),
                I(23, CilOperation.LoadLocal, new CilOperand.Index(1)),
                I(24, CilOperation.Pop),
                I(25, CilOperation.Leave, new CilOperand.BranchTarget(36)),
                I(26, CilOperation.Pop),
                I(27, CilOperation.LoadLocal, new CilOperand.Index(2)),
                I(28, CilOperation.Pop),
                I(29, CilOperation.Leave, new CilOperand.BranchTarget(36)),
                I(30, CilOperation.Call, new CilOperand.Entity(LeafKey)),
                I(31, CilOperation.Leave, new CilOperand.BranchTarget(36)),
                I(32, CilOperation.Pop),
                I(33, CilOperation.LoadLocal, new CilOperand.Index(3)),
                I(34, CilOperation.Pop),
                I(35, CilOperation.Leave, new CilOperand.BranchTarget(36)),
                I(36, CilOperation.Return),
            ])
        {
            ExceptionRegions =
            [
                new(CilExceptionRegionKind.Filter, 8, 6, 18, 4, null, 14),
                new(CilExceptionRegionKind.Catch, 8, 6, 22, 4, TypeKey, null),
                new(CilExceptionRegionKind.Catch, 8, 18, 26, 4, TypeKey, null),
                new(CilExceptionRegionKind.Catch, 30, 2, 32, 4, TypeKey, null),
            ],
        };
        var structured = CreateStructuredMethod(new TypedStackValidator(
            program, program, program, new StackTypeCompatibilityValidator())
            .Validate(CreateGraphBuilder().Build(body)));

        var roots = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            structured, new HashSet<EntityKey> { LeafKey }, new HashSet<string>()));

        foreach (var offset in new[] { 11, 15 })
        {
            for (var local = 0; local < 3; local++)
            {
                Assert.Contains(new RootSource(RootSourceKind.Local, local), roots.Safepoints[offset].Roots);
            }
            Assert.DoesNotContain(new RootSource(RootSourceKind.Local, 3), roots.Safepoints[offset].Roots);
        }
    }

    [Fact]
    public void ThrowingFilterCallPreservesTheValueBeforeALaterOverwrite()
    {
        var program = new FakeProgram();
        var reference = CliTypeIdentity.Named(Assembly, "Roots", "Object", false);
        var body = new CilMethodBody(
            Method(EntryKey, parameters: [CliValueKind.ManagedReference]),
            3,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(1)),
                I(3, CilOperation.LoadNull),
                I(4, CilOperation.LoadNull),
                I(5, CilOperation.CompareExchange, new CilOperand.TypeIdentity(reference)),
                I(6, CilOperation.Pop),
                I(7, CilOperation.Leave, new CilOperand.BranchTarget(20)),
                I(8, CilOperation.Pop),
                I(9, CilOperation.Call, new CilOperand.Entity(LeafKey)),
                I(10, CilOperation.LoadNull),
                I(11, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(12, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(13, CilOperation.EndFilter),
                I(14, CilOperation.Pop),
                I(15, CilOperation.Leave, new CilOperand.BranchTarget(20)),
                I(16, CilOperation.Pop),
                I(17, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(18, CilOperation.Pop),
                I(19, CilOperation.Leave, new CilOperand.BranchTarget(20)),
                I(20, CilOperation.Return),
            ])
        {
            ExceptionRegions =
            [
                new(CilExceptionRegionKind.Filter, 2, 6, 14, 2, null, 8),
                new(CilExceptionRegionKind.Catch, 2, 6, 16, 4, TypeKey, null),
            ],
        };
        var structured = CreateStructuredMethod(new TypedStackValidator(
            program, program, program, new StackTypeCompatibilityValidator())
            .Validate(CreateGraphBuilder().Build(body)));

        var roots = CreateAnalyzer(program).Analyze(new RootMapAnalysisRequest(
            structured, new HashSet<EntityKey> { LeafKey }, new HashSet<string>()));

        Assert.Contains(new RootSource(RootSourceKind.Local, 0), roots.Safepoints[9].Roots);
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
            classifier, program);
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
        new RootDecisionClassifier(
            program,
            program,
            program,
            null,
            new RuntimeAllocationSafepointClassifier(), program);
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
                new RuntimeAllocationSafepointClassifier(), program),
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

    private sealed class FakeProgram(bool leafHasInitializer = false, bool leafIsStatic = true,
        bool delegateType = false, string delegateMethodName = "Invoke", bool delegateMethodStatic = false,
        string? enumIntrinsicName = null, bool enumIntrinsicStatic = true, bool externalAccessor = false) :
        ITypeRepository,
        IFieldRepository,
        IMethodRepository,
        ISymbolFormatter,
        ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) => delegateType && type == DelegateTypeKey;

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
                [LeafKey] = Method(LeafKey, isStatic: leafIsStatic,
                    parameters: leafHasInitializer && leafIsStatic ? [CliValueKind.ManagedReference] : []) with
                {
                    DeclaringType = leafHasInitializer ? GenericTypeKey : TypeKey,
                },
                [ExternalKey] = Method(ExternalKey, isStatic: enumIntrinsicStatic, name: enumIntrinsicName ?? "Method", rva: 0) with
                {
                    DeclaringType = enumIntrinsicName is null ? TypeKey : EnumTypeKey,
                    UnsafeAccessor = externalAccessor ? new(1, "Target", true, false, false) : null,
                },
                [DelegateInvokeKey] = Method(DelegateInvokeKey, isStatic: delegateMethodStatic,
                    name: delegateMethodName, rva: 0) with
                { DeclaringType = DelegateTypeKey },
                [NativeKey] = Method(NativeKey, rva: 0) with
                {
                    NativeImport = new("mule", "collect", System.Reflection.MethodImportAttributes.CallingConventionCDecl,
                        false, false, false, false),
                },
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

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => key == EnumTypeKey
            ? new(key, "System", "Enum", false, [], [ExternalKey])
            : key == GenericTypeKey
            ? new(key, "Roots", "Cache`1", false, [StaticFieldKey],
                [StaticInitializerKey])
            : key == ValueTypeKey
                ? new(key, "Roots", "Pair", true, [], [PairVirtualKey])
                : new(key, "Roots", "Object", false, [],
                    [.. _methods.Values.Where(method => method.DeclaringType == key).Select(method => method.Key)]);

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
