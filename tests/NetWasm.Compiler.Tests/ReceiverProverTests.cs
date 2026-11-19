// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

namespace NetWasm.Compiler.Tests;

// The whole-program proof of methods' this over in-memory programs: Box's
// constructor stores to its this; Run reaches it, and Derived's constructor
// chains to it. Size reads a field of its this, and Over is a virtual method
// that only dispatch reaches.
public sealed class ReceiverProverTests
{
    private const CliValueKind Reference = CliValueKind.ManagedReference;
    private const CliValueKind Integer = CliValueKind.I4;

    private static readonly AssemblyIdentity Assembly = new("ConstructorReceiverUnitTests");
    private static readonly CliTypeIdentity BoxType = CliTypeIdentity.Named(Assembly, "Test", "Box", isValueType: false);
    private static readonly CliTypeIdentity DerivedType = CliTypeIdentity.Named(Assembly, "Test", "Derived", isValueType: false);
    private static readonly MethodInstanceModel Box = Method(0x06000002, ".ctor", BoxType);
    private static readonly MethodInstanceModel Derived = Method(0x06000003, ".ctor", DerivedType);
    private static readonly MethodInstanceModel Run = Method(0x06000004, "Run", BoxType);
    private static readonly MethodInstanceModel Other = Method(0x06000005, "Other", BoxType);
    private static readonly MethodInstanceModel Size = Method(0x06000006, "Size", BoxType);
    private static readonly MethodInstanceModel Over = Method(0x06000007, "Over", DerivedType) with
    {
        Definition = Method(0x06000007, "Over", DerivedType).Definition with { IsVirtual = true },
    };
    private static readonly MethodInstanceModel ActionConstructor = new(new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000008), new EntityKey(Assembly, 0x02000002), ".ctor", false,
            new MethodSignatureModel(CliValueKind.Void, [CliValueKind.ManagedReference, CliValueKind.NativeInt]), 1),
        CliTypeIdentity.Named(Assembly, "System", "Action", isValueType: false), [],
        new MethodSignatureModel(CliValueKind.Void, [CliValueKind.ManagedReference, CliValueKind.NativeInt]));

    // A class that keeps a bare function pointer: its constructor takes only the pointer.
    private static readonly MethodInstanceModel PointerHolderConstructor = new(new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000009), new EntityKey(Assembly, 0x02000003), ".ctor", false,
            new MethodSignatureModel(CliValueKind.Void, [CliValueKind.NativeInt]), 1),
        CliTypeIdentity.Named(Assembly, "Tests", "PointerHolder", isValueType: false), [],
        new MethodSignatureModel(CliValueKind.Void, [CliValueKind.NativeInt]));
    private static readonly CilOperand Field = new CilOperand.Entity(new EntityKey(Assembly, 0x04000001));
    private static readonly CilOperand None = new CilOperand.None();

    // ldarg.0; ldc.i4 1; stfld; ret
    private static readonly (CilOperation, CilOperand)[] StoresToThis =
        [(CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            (CilOperation.StoreField, Field), (CilOperation.Return, None)];
    private static readonly ImmutableArray<CliValueKind>[] StoresToThisStacks = [[], [Reference], [Reference, Integer], []];

    // ldarg.0; ldfld; pop; ret
    private static readonly (CilOperation, CilOperand)[] ReadsThis =
        [(CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadField, Field), (CilOperation.Pop, None),
            (CilOperation.Return, None)];
    private static readonly ImmutableArray<CliValueKind>[] ReadsThisStacks = [[], [Reference], [Integer], []];

    [Fact]
    public void AConstructorReachedOnlyByNewobjStoresToThisUnchecked()
    {
        var program = Prove(Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Box)));

        Assert.Equal([2], NonNull(program, Box));
        Assert.Empty(NonNull(program, Run));
    }

    // A direct call on an object newobj did not produce, a function pointer or a
    // method handle can hand the constructor another object; a callvirt checks the
    // one it passes.
    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.LoadFunction)]
    [InlineData(CilOperation.LoadVirtualFunction)]
    [InlineData(CilOperation.LoadMethodToken)]
    [InlineData(CilOperation.CallVirtual)]
    public void AnyOtherReferenceToTheConstructorButCallvirtDisprovesIt(CilOperation reference)
    {
        // ldnull; call ctor; ret  or  ldnull; ldvirtftn ctor; pop; ret  or  ldftn/ldtoken ctor; pop; ret
        (CilOperation, CilOperand)[] code = reference switch
        {
            CilOperation.Call or CilOperation.CallVirtual =>
                [(CilOperation.LoadNull, None), (reference, new CilOperand.MethodInstance(Box)), (CilOperation.Return, None)],
            CilOperation.LoadVirtualFunction =>
                [(CilOperation.LoadNull, None), (reference, new CilOperand.MethodInstance(Box)), (CilOperation.Pop, None),
                    (CilOperation.Return, None)],
            _ => [(reference, new CilOperand.MethodInstance(Box)), (CilOperation.Pop, None), (CilOperation.Return, None)],
        };
        ImmutableArray<CliValueKind>[] stacks = reference switch
        {
            CilOperation.Call or CilOperation.CallVirtual => [[], [Reference], []],
            CilOperation.LoadVirtualFunction => [[], [Reference], [CliValueKind.NativeInt], []],
            _ => [[], [CliValueKind.NativeInt], []],
        };
        var other = Body(Other, code, stacks);

        var program = Prove(Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Box), other));

        Assert.Equal(reference == CilOperation.CallVirtual ? [2] : [], NonNull(program, Box));
    }

    // Any instance method whose every call is a callvirt reads its this unchecked, even
    // where the caller passes null: the callvirt throws before the method runs.
    [Fact]
    public void AMethodEveryCallOfWhichChecksItsReceiverReadsThisUnchecked()
    {
        var caller = Body(Other, [(CilOperation.LoadNull, None), (CilOperation.CallVirtual, new CilOperand.MethodInstance(Size)),
            (CilOperation.Return, None)], [[], [Reference], []]);

        var program = Prove(Program(Body(Size, ReadsThis, ReadsThisStacks), caller));

        Assert.Equal([1], NonNull(program, Size));
    }

    // A direct call with the caller's own this proves the callee while the caller is
    // proven; once something calls the caller directly on null, neither is.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADirectCallWithTheCallersThisProvesTheCalleeWhileTheCallerIsProven(bool callerOnNull)
    {
        // Run: ldarg.0; call Size; ret
        var run = Body(Run, [(CilOperation.LoadArgument, new CilOperand.Index(0)),
            (CilOperation.Call, new CilOperand.MethodInstance(Size)), (CilOperation.Return, None)], [[], [Reference], []]);
        var other = Body(Other, [(CilOperation.LoadNull, None),
            (callerOnNull ? CilOperation.Call : CilOperation.CallVirtual, new CilOperand.MethodInstance(Run)),
            (CilOperation.Return, None)], [[], [Reference], []]);

        var program = Prove(Program(Body(Size, ReadsThis, ReadsThisStacks), run, other));

        Assert.Equal(callerOnNull ? [] : [1], NonNull(program, Size));
        Assert.Equal(callerOnNull ? [] : [1], NonNull(program, Run));
    }

    // An override only dispatch reaches stays proven while ldvirtftn's pointers go
    // straight into delegates; a pointer kept for a direct call, or handed to a
    // constructor that is no delegate's, could pass any object.
    [Theory]
    [InlineData("delegate")]
    [InlineData("kept")]
    [InlineData("another constructor")]
    public void APointerLdvirtftnKeepsDisprovesEveryVirtualMethod(string use)
    {
        // Other: ldnull; ldnull; ldvirtftn Size; then newobj Action; pop, or pop; pop, or newobj PointerHolder; pop; pop; ret
        (CilOperation, CilOperand)[] consume = use switch
        {
            "delegate" => [(CilOperation.NewObject, new CilOperand.MethodInstance(ActionConstructor)), (CilOperation.Pop, None)],
            "another constructor" => [(CilOperation.NewObject, new CilOperand.MethodInstance(PointerHolderConstructor)),
                (CilOperation.Pop, None), (CilOperation.Pop, None)],
            _ => [(CilOperation.Pop, None), (CilOperation.Pop, None)],
        };
        (CilOperation, CilOperand)[] code = [(CilOperation.LoadNull, None), (CilOperation.LoadNull, None),
            (CilOperation.LoadVirtualFunction, new CilOperand.MethodInstance(Size)), .. consume, (CilOperation.Return, None)];
        ImmutableArray<CliValueKind>[] stacks = use switch
        {
            "delegate" => [[], [Reference], [Reference, Reference], [Reference, CliValueKind.NativeInt], [Reference], []],
            "another constructor" => [[], [Reference], [Reference, Reference], [Reference, CliValueKind.NativeInt],
                [Reference, Reference], [Reference], []],
            _ => [[], [Reference], [Reference, Reference], [Reference, CliValueKind.NativeInt], [Reference], []],
        };

        var program = Prove(Program(Body(Over, ReadsThis, ReadsThisStacks), Body(Other, code, stacks)));

        Assert.Equal(use == "delegate" ? [1] : [], NonNull(program, Over));
    }

    // A constructor that chains to Box's with its own this proves Box's this as long
    // as its own is proven; once a caller hands it another object, both lose it.
    // F# chains with callvirt, which only adds a null check.
    [Theory]
    [InlineData(false, CilOperation.Call)]
    [InlineData(true, CilOperation.Call)]
    [InlineData(false, CilOperation.CallVirtual)]
    public void AChainedConstructorIsProvenWhileTheOneChainingToItIs(bool derivedCalledOnNull, CilOperation chain)
    {
        // Derived: ldarg.0; call (or callvirt) Box; ldarg.0; ldc.i4 1; stfld; ret
        var derived = Body(Derived,
            [(CilOperation.LoadArgument, new CilOperand.Index(0)), (chain, new CilOperand.MethodInstance(Box)),
                (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                (CilOperation.StoreField, Field), (CilOperation.Return, None)],
            [[], [Reference], [], [Reference], [Reference, Integer], []]);
        ManagedMethodBody[] extra = derivedCalledOnNull
            ? [Body(Other, [(CilOperation.LoadNull, None), (CilOperation.Call, new CilOperand.MethodInstance(Derived)),
                (CilOperation.Return, None)], [[], [Reference], []])]
            : [];

        var program = Prove(Program(Body(Box, StoresToThis, StoresToThisStacks), Constructs(Derived), [derived, .. extra]));

        Assert.Equal(derivedCalledOnNull ? [] : [2], NonNull(program, Box));
        // Derived's chained call takes its this as an object too.
        Assert.Equal(derivedCalledOnNull ? [] : [1, 4], NonNull(program, Derived));
    }

    // A constructor that a delegate binds to, or member execution invokes, may get any object.
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

        Assert.Empty(NonNull(Prove(program), Box));
    }

    // A constructed method's body is replaced where the program keeps it, and a
    // constructor that never touches its this, or a disabled prover, changes nothing.
    [Fact]
    public void ConstructedConstructorsAreProvenInPlaceAndNothingElseChanges()
    {
        var constructed = Body(Box, StoresToThis, StoresToThisStacks);
        var program = Program(null, Constructs(Box)) with
        {
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(Box.CanonicalName, constructed),
        };

        Assert.Equal([2], Prove(program).ConstructedMethods[Box.CanonicalName].Body.ArrayBoundsProof!.NonNullOffsets);
        var untouched = Program(Body(Box, [(CilOperation.Return, None)], [[]]), Constructs(Box));
        Assert.Same(untouched.Methods[Box.Definition.Key], Prove(untouched).Methods[Box.Definition.Key]);
        Assert.Same(program, Prover(enabled: false).Prove(program));
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        var calls = new Calls();
        var planner = Planner(calls);
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var stacks = new StackTable([]);
        Assert.Throws<ArgumentNullException>(() => new ReceiverProver(null!, calls, graphs, stacks, true));
        Assert.Throws<ArgumentNullException>(() => new ReceiverProver(planner, null!, graphs, stacks, true));
        Assert.Throws<ArgumentNullException>(() => new ReceiverProver(planner, calls, null!, stacks, true));
        Assert.Throws<ArgumentNullException>(() => new ReceiverProver(planner, calls, graphs, null!, true));
        Assert.Throws<ArgumentNullException>(() => Prover(enabled: true).Prove(null!));
    }

    private static readonly Dictionary<string, ImmutableArray<CliValueKind>[]> Stacks = [];

    private static MethodInstanceModel Method(int token, string name, CliTypeIdentity type)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        return new(new MethodDefinitionModel(new EntityKey(Assembly, token), new EntityKey(Assembly, 0x02000001), name,
            false, signature, 1), type, [], signature);
    }

    // Run: newobj; pop; ret
    private static ManagedMethodBody Constructs(MethodInstanceModel constructor) => Body(Run,
        [(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)), (CilOperation.Pop, None), (CilOperation.Return, None)],
        [[], [Reference], []]);

    private static ManagedMethodBody Body(
        MethodInstanceModel method, (CilOperation Operation, CilOperand Operand)[] code, ImmutableArray<CliValueKind>[] stacks)
    {
        lock (Stacks) Stacks[method.CanonicalName] = stacks;
        var body = new CilMethodBody(method.Definition, 4, [],
            [.. code.Select((instruction, offset) => new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))])
        {
            MethodInstance = method,
        };
        return new ManagedMethodBody(method, new StackTable(stacks).Validate(new ControlFlowGraphBuilderFactory().Create().Build(body)));
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

    private static ImmutableHashSet<int> NonNull(ReachableProgram program, MethodInstanceModel method) =>
        program.Methods[method.Definition.Key].Body.ArrayBoundsProof?.NonNullOffsets ?? [];

    private static ReachableProgram Prove(ReachableProgram program) => Prover(enabled: true).Prove(program);

    private static ReceiverProver Prover(bool enabled)
    {
        var calls = new Calls();
        return new(Planner(calls), calls, new ControlFlowGraphBuilderFactory().Create(), new StackTable(null), enabled);
    }

    private static ConstructedReceiverPlanner Planner(Calls calls) =>
        new(new ControlFlowDominanceAnalyzer(), new StackEffectResolver(calls), calls);

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
