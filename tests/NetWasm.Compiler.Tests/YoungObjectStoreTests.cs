// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Generations;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Tests;

// Which stores a collector with generations need not be told of, over in-memory programs: Box's constructor stores
// to its this; Run reaches it; Derived's constructor chains to it; Other references it otherwise.
public sealed class YoungObjectStoreTests
{
    private const CliValueKind Reference = CliValueKind.ManagedReference;
    private const CliValueKind Integer = CliValueKind.I4;

    private static readonly AssemblyIdentity Assembly = new("YoungObjectStoreUnitTests");
    private static readonly CliTypeIdentity BoxType = CliTypeIdentity.Named(Assembly, "Test", "Box", isValueType: false);
    private static readonly CliTypeIdentity DerivedType = CliTypeIdentity.Named(Assembly, "Test", "Derived", isValueType: false);
    private static readonly CliTypeIdentity ValueType = CliTypeIdentity.Named(Assembly, "Test", "Value", isValueType: true);
    private static readonly MethodInstanceModel Box = Method(0x06000002, ".ctor", BoxType);
    private static readonly MethodInstanceModel Derived = Method(0x06000003, ".ctor", DerivedType);
    private static readonly MethodInstanceModel Run = Method(0x06000004, "Run", BoxType, isStatic: true);
    private static readonly MethodInstanceModel Other = Method(0x06000005, "Other", BoxType, isStatic: true);
    private static readonly MethodInstanceModel ValueConstructor = Method(0x06000006, ".ctor", ValueType);
    private static readonly CilOperand Field = new CilOperand.Entity(new EntityKey(Assembly, 0x04000001));
    private static readonly CilOperand None = new CilOperand.None();
    private static readonly CilOperand This = new CilOperand.Index(0);

    // ldarg.0; ldnull; stfld; ret
    private static readonly (CilOperation, CilOperand)[] StoresToThis =
        [(CilOperation.LoadArgument, This), (CilOperation.LoadNull, None), (CilOperation.StoreField, Field), (CilOperation.Return, None)];
    private static readonly ImmutableArray<CliValueKind>[] StoresToThisStacks = [[], [Reference], [Reference, Reference], []];

    [Fact]
    public void AConstructorOnlyNewobjReachesStartsWithAYoungThis()
    {
        var program = Prove(Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Box)));

        Assert.True(Young(program, Box));
        Assert.False(Young(program, Run));
    }

    // A direct call can pass any object, as can a function pointer, a method handle and a callvirt.
    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.CallVirtual)]
    [InlineData(CilOperation.LoadFunction)]
    [InlineData(CilOperation.LoadMethodToken)]
    public void AnyOtherReferenceDisprovesTheConstructor(CilOperation reference)
    {
        (CilOperation, CilOperand)[] code = reference is CilOperation.Call or CilOperation.CallVirtual
            ? [(CilOperation.LoadNull, None), (reference, new CilOperand.MethodInstance(Box)), (CilOperation.Return, None)]
            : [(reference, new CilOperand.MethodInstance(Box)), (CilOperation.Pop, None), (CilOperation.Return, None)];
        ImmutableArray<CliValueKind>[] stacks = reference is CilOperation.Call or CilOperation.CallVirtual
            ? [[], [Reference], []]
            : [[], [CliValueKind.NativeInt], []];

        var program = Prove(Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Box), Body(Other, code, stacks)));

        Assert.False(Young(program, Box));
    }

    // Derived chains to Box with its this, after a dup, before anything collects: Box starts young while Derived
    // does. A call of Derived on another object disproves both.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConstructorChainedToFirstIsYoungWhileTheOneChainingToItIs(bool derivedCalledOnAnother)
    {
        var derived = Body(Derived,
            [(CilOperation.LoadArgument, This), (CilOperation.Duplicate, None), (CilOperation.Pop, None),
                (CilOperation.Call, new CilOperand.MethodInstance(Box)), (CilOperation.Return, None)],
            [[], [Reference], [Reference, Reference], [Reference], []]);
        ManagedMethodBody[] extra = derivedCalledOnAnother
            ? [Body(Other, [(CilOperation.LoadNull, None), (CilOperation.Call, new CilOperand.MethodInstance(Derived)),
                (CilOperation.Return, None)], [[], [Reference], []])]
            : [];

        var program = Prove(Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Derived), [derived, .. extra]));

        Assert.Equal(!derivedCalledOnAnother, Young(program, Box));
        Assert.Equal(!derivedCalledOnAnother, Young(program, Derived));
    }

    // A chain after an allocation, a branch or a call, or on another object, passes a this that may be old; so
    // does one in a constructor with exception regions or one that writes its this.
    public static TheoryData<string> LateChains() =>
        ["allocation", "branch", "call", "another object", "handler", "writes this", "static"];

    [Theory]
    [MemberData(nameof(LateChains))]
    public void AChainThatMayPassAnOldThisDisprovesTheConstructorItReaches(string shape)
    {
        var chain = (CilOperation.Call, (CilOperand)new CilOperand.MethodInstance(Box));
        var (code, stacks) = shape switch
        {
            "allocation" => (new[] { (CilOperation.LoadInt32, (CilOperand)new CilOperand.ConstantI4(1)),
                    (CilOperation.NewArray, None), (CilOperation.Pop, None), (CilOperation.LoadArgument, This), chain,
                    (CilOperation.Return, None) },
                new ImmutableArray<CliValueKind>[] { [], [Integer], [Reference], [], [Reference], [] }),
            "branch" => ([(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.BranchIfTrue,
                    new CilOperand.BranchTarget(2)), (CilOperation.LoadArgument, This), chain, (CilOperation.Return, None)],
                [[], [Integer], [], [Reference], []]),
            "call" => ([(CilOperation.Call, new CilOperand.MethodInstance(Run)), (CilOperation.LoadArgument, This), chain,
                    (CilOperation.Return, None)],
                [[], [], [Reference], []]),
            "another object" => ([(CilOperation.LoadNull, None), chain, (CilOperation.Return, None)], [[], [Reference], []]),
            "static" => ([(CilOperation.LoadStaticField, Field), (CilOperation.Pop, None), (CilOperation.LoadArgument, This),
                    chain, (CilOperation.Return, None)],
                [[], [Reference], [], [Reference], []]),
            // ldarg.0; call; leave 5; nop; endfinally; ret, the first three in a try with a finally.
            "handler" => ([(CilOperation.LoadArgument, This), chain, (CilOperation.Leave, new CilOperand.BranchTarget(5)),
                    (CilOperation.Nop, None), (CilOperation.EndFinally, None), (CilOperation.Return, None)],
                [[], [Reference], [], [], [], []]),
            _ => ([(CilOperation.LoadArgument, This), chain, (CilOperation.Return, None)], [[], [Reference], []]),
        };
        if (shape == "writes this")
        {
            code = [(CilOperation.LoadArgumentAddress, This), (CilOperation.Pop, None), .. code];
            stacks = [[], [CliValueKind.ManagedAddress], .. stacks];
        }
        var derived = shape == "handler"
            ? Body(Derived, code, stacks, [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 3, 3, 2, null, null)])
            : Body(Derived, code, stacks);

        var program = Prove(Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Derived), derived));

        Assert.False(Young(program, Box));
        Assert.True(Young(program, Derived));
        Assert.Empty(YoungConstructorChains.Of(Derived, program.Methods[Derived.Definition.Key].Body));
    }

    // A constructor a delegate binds to, or member execution invokes, may get any object.
    [Theory]
    [InlineData("delegate")]
    [InlineData("descriptor")]
    [InlineData("target")]
    [InlineData("dynamic invoke")]
    public void InvokedConstructorsAreDisproven(string invocation)
    {
        var program = Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Box));
        var value = new ManagedDelegateValueBinding(BoxType, BoxType, ManagedDelegateAdaptation.Identity);
        program = invocation switch
        {
            "delegate" => program with
            {
                DelegateBindings = [new ManagedDelegateBinding(new ManagedMethodIdentity(Run.CanonicalName),
                    new ManagedMethodIdentity(Box.CanonicalName), Run, Box, [], value)],
            },
            "descriptor" => program with { MemberExecution = Members(new MemberMethodExecutionPlan(Box, false, [])) },
            "target" => program with
            {
                MemberExecution = Members(new MemberMethodExecutionPlan(Run, true, [new DispatchTargetModel(BoxType, Box)])),
            },
            _ => program with
            {
                MemberExecution = MemberExecutionPlan.Empty with
                {
                    DelegateInvocation = new DelegateDynamicInvokePlan(
                        ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(Box.CanonicalName, Box), [], Run, Run, Run, Run),
                },
            },
        };

        Assert.False(Young(Prove(program), Box));
    }

    // A constructed method's body is replaced where the program keeps it; a value type's constructor, and one
    // already young, are left alone.
    [Fact]
    public void ConstructedConstructorsAreProvenInPlaceAndNothingElseChanges()
    {
        var constructed = Body(Box, StoresToThis, StoresToThisStacks);
        var program = Program(null, Constructs(Box)) with
        {
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(Box.CanonicalName, constructed),
        };
        Assert.True(Prove(program).ConstructedMethods[Box.CanonicalName].Body.YoungThisAtEntry);

        var value = Program(Body(ValueConstructor, [(CilOperation.Return, None)], [[]]), Constructs(Box));
        Assert.Same(value.Methods[ValueConstructor.Definition.Key], Prove(value).Methods[ValueConstructor.Definition.Key]);
        var proven = Prove(Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Box)));
        Assert.Same(proven.Methods[Box.Definition.Key], Prove(proven).Methods[Box.Definition.Key]);
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        var calls = new Calls();
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var stacks = new StackTable([]);
        Assert.Throws<ArgumentNullException>(() => new YoungConstructorProver(null!, graphs, stacks));
        Assert.Throws<ArgumentNullException>(() => new YoungConstructorProver(calls, null!, stacks));
        Assert.Throws<ArgumentNullException>(() => new YoungConstructorProver(calls, graphs, null!));
        Assert.Throws<ArgumentNullException>(() => Prover().Prove(null!));
    }

    // The finder: stores into an object the method allocated with no safepoint since.
    [Fact]
    public void AStoreIntoAnObjectAllocatedWithNoSafepointSinceIsUnrecorded()
    {
        // newobj Box; dup; ldsfld; stfld; newarr; dup; ldc 0; ldsfld; stelem.ref; pop; ret
        var body = Body(Run,
            [(CilOperation.NewObject, new CilOperand.MethodInstance(Box)), (CilOperation.Duplicate, None),
                (CilOperation.LoadStaticField, Field), (CilOperation.StoreField, Field), (CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                (CilOperation.NewArray, None), (CilOperation.Duplicate, None), (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                (CilOperation.LoadStaticField, Field), (CilOperation.StoreArrayElementReference, None), (CilOperation.Pop, None),
                (CilOperation.Return, None)],
            [[], [Reference], [Reference, Reference], [Reference, Reference, Reference], [], [Integer], [Reference],
                [Reference, Reference], [Reference, Reference, Integer], [Reference, Reference, Integer, Reference], [Reference], []]);

        Assert.Equal([3, 9], Find(body, Safepoint(0), Safepoint(5)));
        // A constructor that can collect may age the object it was handed.
        Assert.Equal([9], Find(body, Safepoint(0, collectingConstructor: true), Safepoint(5)));
        // So may a safepoint between the allocation and the store.
        Assert.Equal([9], Find(body, Safepoint(0), Safepoint(2), Safepoint(5)));
    }

    // A store of null needs no record wherever it stores: into a field of any object, an element of any array, or
    // through an address; a safepoint between the null and its store changes nothing.
    [Fact]
    public void AStoreOfNullIsUnrecordedWhereverItStores()
    {
        // ldarg.0; ldnull; call Run; stfld; ldarg.0; ldc 0; ldnull; stelem.ref; ldarg.0; ldnull; stobj;
        // ldarg.0; ldarg.0; stfld; ret
        var body = Body(Box,
            [(CilOperation.LoadArgument, This), (CilOperation.LoadNull, None), (CilOperation.Call, new CilOperand.MethodInstance(Run)),
                (CilOperation.StoreField, Field), (CilOperation.LoadArgument, This), (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                (CilOperation.LoadNull, None), (CilOperation.StoreArrayElementReference, None), (CilOperation.LoadArgument, This),
                (CilOperation.LoadNull, None), (CilOperation.StoreObject, Field), (CilOperation.LoadArgument, This),
                (CilOperation.LoadArgument, This), (CilOperation.StoreField, Field), (CilOperation.Return, None)],
            [[], [Reference], [Reference, Reference], [Reference, Reference], [], [Reference], [Reference, Integer],
                [Reference, Integer, Reference], [], [Reference], [Reference, Reference], [], [Reference], [Reference, Reference], []]);

        // The call is a safepoint, and an instruction whose effect only its method decides: the null is forgotten.
        Assert.Equal([7, 10], Find(body, Safepoint(2)));
        // Without the call's effect forgotten, a safepoint keeps the null.
        Assert.Equal([7, 10], Find(body, Safepoint(1), Safepoint(6)));
    }

    // A local follows the object until a safepoint, a store of something else, or never when its address is taken.
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ALocalFollowsTheObjectItHolds(bool addressTaken, bool overwritten, bool unrecorded)
    {
        // newobj; stloc 0; [ldnull; stloc 0;] ldloc 0; ldsfld; stfld; [ldloca 0; pop;] ret
        List<(CilOperation, CilOperand)> code = [(CilOperation.NewObject, new CilOperand.MethodInstance(Box)),
            (CilOperation.StoreLocal, new CilOperand.Index(0))];
        List<ImmutableArray<CliValueKind>> stacks = [[], [Reference]];
        if (overwritten)
        {
            code.AddRange([(CilOperation.LoadNull, None), (CilOperation.StoreLocal, new CilOperand.Index(0))]);
            stacks.AddRange([[], [Reference]]);
        }
        code.AddRange([(CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.LoadStaticField, Field), (CilOperation.StoreField, Field)]);
        stacks.AddRange([[], [Reference], [Reference, Reference]]);
        if (addressTaken)
        {
            code.AddRange([(CilOperation.LoadLocalAddress, new CilOperand.Index(0)), (CilOperation.Pop, None)]);
            stacks.AddRange([[], [CliValueKind.ManagedAddress]]);
        }
        code.Add((CilOperation.Return, None));
        stacks.Add([]);
        var body = Body(Run, [.. code], [.. stacks], locals: [Reference]);

        Assert.Equal(unrecorded ? [overwritten ? 6 : 4] : [], Find(body, Safepoint(0)));
    }

    // An instruction whose effect depends on the method it names forgets the stack, though not the locals.
    [Fact]
    public void ACallForgetsTheStackButNotTheLocals()
    {
        // newobj; dup; stloc 0; call Run; ldloc 0; ldsfld; stfld; ldsfld; stfld; ret
        var body = Body(Other,
            [(CilOperation.NewObject, new CilOperand.MethodInstance(Box)), (CilOperation.Duplicate, None),
                (CilOperation.StoreLocal, new CilOperand.Index(0)), (CilOperation.Call, new CilOperand.MethodInstance(Run)),
                (CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.LoadStaticField, Field), (CilOperation.StoreField, Field),
                (CilOperation.LoadStaticField, Field), (CilOperation.StoreField, Field), (CilOperation.Return, None)],
            [[], [Reference], [Reference, Reference], [Reference], [Reference], [Reference, Reference],
                [Reference, Reference, Reference], [Reference], [Reference, Reference], []],
            locals: [Reference]);

        Assert.Equal([6], Find(body, Safepoint(0)));
    }

    // In a constructor whose this is young when it starts, this is young until the first safepoint, in every block
    // each path from the start reaches without one; not in a handler, nor once the constructor writes its this.
    [Theory]
    [InlineData("straight", new[] { 2, 9 })]
    [InlineData("collects first", new int[0])]
    [InlineData("writes this", new int[0])]
    [InlineData("not young", new int[0])]
    public void AYoungThisIsYoungUntilItsFirstSafepoint(string shape, int[] expected)
    {
        // ldarg.0; ldsfld; stfld; ldc 1; brtrue 7; ldarg.0; pop; ldarg.0; ldsfld; stfld; ret
        (CilOperation, CilOperand)[] code =
        [
            (CilOperation.LoadArgument, This), (CilOperation.LoadStaticField, Field), (CilOperation.StoreField, Field),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(7)),
            (shape == "writes this" ? CilOperation.LoadArgumentAddress : CilOperation.LoadArgument, This), (CilOperation.Pop, None),
            (CilOperation.LoadArgument, This), (CilOperation.LoadStaticField, Field), (CilOperation.StoreField, Field),
            (CilOperation.Return, None),
        ];
        ImmutableArray<CliValueKind>[] stacks =
        [
            [], [Reference], [Reference, Reference], [], [Integer],
            [], [shape == "writes this" ? CliValueKind.ManagedAddress : Reference], [], [Reference], [Reference, Reference], [],
        ];
        var body = Body(Box, code, stacks);
        if (shape != "not young") body = Flag(body);

        Assert.Equal(expected, Find(body, shape == "collects first" ? [Safepoint(0)] : []).Order());
    }

    // A handler starts with this aged, as may the code after it.
    [Fact]
    public void AHandlerStartsWithAnOldThis()
    {
        // ldarg.0; ldsfld; stfld; leave 7; ldarg.0; pop; endfinally; ldarg.0; ldsfld; stfld; ret, a try of the first four
        var body = Flag(Body(Box,
            [(CilOperation.LoadArgument, This), (CilOperation.LoadStaticField, Field), (CilOperation.StoreField, Field),
                (CilOperation.Leave, new CilOperand.BranchTarget(7)), (CilOperation.LoadArgument, This), (CilOperation.Pop, None),
                (CilOperation.EndFinally, None), (CilOperation.LoadArgument, This), (CilOperation.LoadStaticField, Field),
                (CilOperation.StoreField, Field), (CilOperation.Return, None)],
            [[], [Reference], [Reference, Reference], [], [], [Reference], [], [], [Reference], [Reference, Reference], []],
            [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 4, 4, 3, null, null)]));

        Assert.Equal([2], Find(body));
    }

    // Whether this is young decides which stores a compiled unit records, so the unit's fingerprint covers it; and
    // the constructors a body chains to with its young this are part of what it contributes, so that an edit moving
    // an allocation before the chain is not one an early cutoff keeps.
    [Fact]
    public void TheFingerprintsCoverAYoungThisAndTheChainsThatGiveIt()
    {
        var box = Body(Box, StoresToThis, StoresToThisStacks);
        Assert.True(UnitPrint(box) != UnitPrint(Flag(box)));

        (CilOperation, CilOperand)[] allocation =
            [(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.NewArray, None), (CilOperation.Pop, None)];
        (CilOperation, CilOperand)[] chain =
            [(CilOperation.LoadArgument, This), (CilOperation.Call, new CilOperand.MethodInstance(Box))];
        var first = Body(Derived, [.. chain, .. allocation, (CilOperation.Return, None)],
            [[], [Reference], [], [Integer], [Reference], []]);
        var late = Body(Derived, [.. allocation, .. chain, (CilOperation.Return, None)],
            [[], [Integer], [Reference], [], [Reference], []]);
        Assert.NotEqual(Contribution(first), Contribution(late));
    }

    [Fact]
    public void TheFinderRejectsMissingInput()
    {
        var body = Body(Run, [(CilOperation.Return, None)], [[]]);
        var finder = new YoungObjectStoreFinder();
        Assert.Throws<ArgumentNullException>(() => finder.Find(null!, ImmutableDictionary<int, SafepointRootMap>.Empty));
        Assert.Throws<ArgumentNullException>(() => finder.Find(body, null!));
        Assert.Empty(finder.Find(body, ImmutableDictionary<int, SafepointRootMap>.Empty));
    }

    private static readonly Dictionary<string, ImmutableArray<CliValueKind>[]> Stacks = [];

    private static MethodInstanceModel Method(int token, string name, CliTypeIdentity type, bool isStatic = false)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        return new(new MethodDefinitionModel(new EntityKey(Assembly, token), new EntityKey(Assembly, 0x02000001), name,
            isStatic, signature, 1), type, [], signature);
    }

    // Run: newobj; pop; ret
    private static ManagedMethodBody Constructs(MethodInstanceModel constructor) => Body(Run,
        [(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)), (CilOperation.Pop, None), (CilOperation.Return, None)],
        [[], [Reference], []]);

    private static ManagedMethodBody Body(
        MethodInstanceModel method, (CilOperation Operation, CilOperand Operand)[] code, ImmutableArray<CliValueKind>[] stacks,
        ImmutableArray<CilExceptionRegion> regions = default, ImmutableArray<CliValueKind> locals = default)
    {
        lock (Stacks) Stacks[method.CanonicalName] = stacks;
        var body = new CilMethodBody(method.Definition, 4, locals.IsDefault ? [] : locals,
            [.. code.Select((instruction, offset) => new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))])
        {
            MethodInstance = method,
            ExceptionRegions = regions.IsDefault ? [] : regions,
        };
        return new ManagedMethodBody(method, new StackTable(stacks).Validate(new ControlFlowGraphBuilderFactory().Create().Build(body)));
    }

    private static ManagedMethodBody Flag(ManagedMethodBody method)
    {
        lock (Stacks) return new ManagedMethodBody(method.Method, new StackTable(Stacks[method.Method.CanonicalName])
            .Validate(new ControlFlowGraphBuilderFactory().Create().Build(method.Body with { YoungThisAtEntry = true })));
    }

    private static ReachableProgram Program(ManagedMethodBody? constructor, ManagedMethodBody run, params ManagedMethodBody[] others)
    {
        var methods = new[] { constructor, run }.OfType<ManagedMethodBody>().Concat(others)
            .ToImmutableDictionary(method => method.Method.Definition.Key);
        return ValidationTestData.Program() with { Methods = methods };
    }

    private static MemberExecutionPlan Members(MemberMethodExecutionPlan plan) => MemberExecutionPlan.Empty with
    {
        Methods = ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty.Add(plan.Descriptor.CanonicalName, plan),
    };

    private static bool Young(ReachableProgram program, MethodInstanceModel method) =>
        program.Methods[method.Definition.Key].Body.YoungThisAtEntry;

    private static ReachableProgram Prove(ReachableProgram program) => Prover().Prove(program);

    private static YoungConstructorProver Prover() =>
        new(new Calls(), new ControlFlowGraphBuilderFactory().Create(), new StackTable(null));

    private static SafepointRootMap Safepoint(int offset, bool collectingConstructor = false) => new(offset, [],
        collectingConstructor ? [new RootSource(RootSourceKind.AllocationTemporary, 0)] : []);

    private static NetWasm.Compiler.Wasm.Units.UnitFingerprint UnitPrint(ManagedMethodBody body)
    {
        var builder = new NetWasm.Compiler.Wasm.Units.UnitFingerprintBuilder();
        builder.Add(body);
        return builder.Finish();
    }

    private static NetWasm.Compiler.Metadata.Inputs.InputFingerprint Contribution(ManagedMethodBody body) =>
        MethodContributionFingerprint.Of(new ReachableMethodAnalysis(body.Method, body, [], [],
            new ReachabilityInstructionAnalysis([], [], [], [], [], [], [], [], [], [], [])));

    private static ImmutableHashSet<int> Find(ManagedMethodBody body, params SafepointRootMap[] safepoints) =>
        new YoungObjectStoreFinder().Find(body, safepoints.ToImmutableDictionary(safepoint => safepoint.IlOffset));

    private sealed class Calls : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operation is CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject or
                CilOperation.LoadFunction or CilOperation.LoadVirtualFunction
                ? (instruction.Operand as CilOperand.MethodInstance)?.Value
                : null;
    }

    // Each body's entry stacks, by method, as validation found them.
    private sealed class StackTable(ImmutableArray<CliValueKind>[]? stacks) : ITypedStackValidator
    {
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph)
        {
            ImmutableArray<CliValueKind>[] table;
            lock (Stacks) table = stacks ?? Stacks[graph.MethodBody.MethodInstance!.CanonicalName];
            return new(graph, graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty),
                table.Select((stack, offset) => (stack, offset)).ToImmutableDictionary(entry => entry.offset, entry => entry.stack));
        }
    }
}
