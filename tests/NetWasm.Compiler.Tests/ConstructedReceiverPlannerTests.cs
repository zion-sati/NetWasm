using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The planner over in-memory bodies, for the IL that C# never writes: a this that is
// stored or exposed, a slot read before its one store, a store that does not
// dominate the read, a block whose stacks it cannot follow, and the receiver kinds.
public sealed class ConstructedReceiverPlannerTests
{
    private const CliValueKind Reference = CliValueKind.ManagedReference;
    private const CliValueKind Integer = CliValueKind.I4;

    private static readonly AssemblyIdentity Assembly = new("ConstructedReceiverUnitTests");
    private static readonly CliTypeIdentity Box = CliTypeIdentity.Named(Assembly, "Test", "Box", isValueType: false);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Test", "Pair", isValueType: true);
    private static readonly MethodInstanceModel Constructor = Method(0x06000002, ".ctor", Box, CliValueKind.Void);
    private static readonly MethodInstanceModel PairConstructor = Method(0x06000003, ".ctor", Pair, CliValueKind.Void);
    private static readonly MethodInstanceModel Set = Method(0x06000004, "Set", Box, CliValueKind.Void, Integer);
    private static readonly MethodInstanceModel Owner = Method(0x06000005, "Run", Box, CliValueKind.Void, Integer);
    private static readonly CilOperand Field = new CilOperand.Entity(new EntityKey(Assembly, 0x04000001));
    private static readonly CilOperand None = new CilOperand.None();

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AProvenThisCountsOnlyWhileTheCallerSaysSo(bool thisIsNonNull, bool proven)
    {
        // ldarg.0; ldc.i4 1; stfld; ret
        var flow = Flow([(CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            (CilOperation.StoreField, Field), (CilOperation.Return, None)], [[], [Reference], [Reference, Integer], []]);

        Assert.Equal(proven, Plan(flow, thisIsNonNull).Contains(2));
    }

    // A this the method stores to or exposes by address may no longer be the object it was given.
    [Theory]
    [InlineData(CilOperation.LoadArgumentAddress)]
    [InlineData(CilOperation.StoreArgument)]
    public void AThisTheMethodStoresOrExposesIsNotProven(CilOperation change)
    {
        // ldarga.0; pop (or ldnull; starg.0); ldarg.0; ldc.i4 1; stfld; ret
        var first = change == CilOperation.StoreArgument
            ? (CilOperation.LoadNull, None)
            : (change, new CilOperand.Index(0));
        var second = change == CilOperation.StoreArgument
            ? (change, new CilOperand.Index(0))
            : (CilOperation.Pop, None);
        var flow = Flow([first, second, (CilOperation.LoadArgument, new CilOperand.Index(0)),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.StoreField, Field), (CilOperation.Return, None)],
            [[], [change == CilOperation.StoreArgument ? Reference : CliValueKind.ManagedAddress], [], [Reference],
                [Reference, Integer], []]);

        Assert.Empty(Plan(flow, thisIsNonNull: true));
    }

    // newobj; ldflda; pop: the address of a field of a new object, and a direct
    // constructor call on one, as IL may make to run a constructor again.
    [Fact]
    public void FieldAddressesAndDirectConstructorCallsOfANewObjectAreProven()
    {
        var flow = Flow([(CilOperation.NewObject, new CilOperand.MethodInstance(Constructor)), (CilOperation.Duplicate, None),
            (CilOperation.LoadFieldAddress, Field), (CilOperation.Pop, None),
            (CilOperation.Call, new CilOperand.MethodInstance(Constructor)), (CilOperation.Return, None)],
            [[], [Reference], [Reference, Reference], [Reference, CliValueKind.ManagedAddress], [Reference], []]);

        Assert.Equal([2, 4], Plan(flow, thisIsNonNull: false).Order());
    }

    // A callvirt's object lies beneath its arguments.
    [Fact]
    public void ACallvirtOnANewObjectIsProvenBeneathItsArguments()
    {
        // newobj; ldc.i4 1; callvirt Set(int); ret
        var flow = Flow([(CilOperation.NewObject, new CilOperand.MethodInstance(Constructor)),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.CallVirtual, new CilOperand.MethodInstance(Set)),
            (CilOperation.Return, None)], [[], [Reference], [Reference, Integer], []]);

        Assert.Equal([2], Plan(flow, thisIsNonNull: false));
    }

    // A value type's newobj makes a value, and its field accesses take an address.
    [Fact]
    public void AValueTypeConstructionIsNoObject()
    {
        // ldloca.0; ldfld; pop; newobj Pair; pop; ret
        var flow = Flow([(CilOperation.LoadLocalAddress, new CilOperand.Index(0)), (CilOperation.LoadField, Field),
            (CilOperation.Pop, None), (CilOperation.NewObject, new CilOperand.MethodInstance(PairConstructor)),
            (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [CliValueKind.ManagedAddress], [Integer], [], [CliValueKind.ValueType], []]);

        Assert.Empty(Plan(flow, thisIsNonNull: false));
    }

    // A value loaded from the slot before its one store, still on the stack or read
    // after it in the same block, is the one the block entered with.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASlotReadBeforeItsOneStoreIsNotProven(bool keptOnStack)
    {
        // ldloc.0; ldfld; pop; newobj; stloc.0; ret  or  ldloc.0; newobj; stloc.0; ldfld; pop; ret
        (CilOperation, CilOperand)[] code = keptOnStack
            ? [(CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.NewObject, new CilOperand.MethodInstance(Constructor)),
                (CilOperation.StoreLocal, new CilOperand.Index(0)), (CilOperation.LoadField, Field), (CilOperation.Pop, None),
                (CilOperation.Return, None)]
            : [(CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.LoadField, Field), (CilOperation.Pop, None),
                (CilOperation.NewObject, new CilOperand.MethodInstance(Constructor)), (CilOperation.StoreLocal, new CilOperand.Index(0)),
                (CilOperation.Return, None)];
        ImmutableArray<CliValueKind>[] stacks = keptOnStack
            ? [[], [Reference], [Reference, Reference], [Reference], [Integer], []]
            : [[], [Reference], [Integer], [], [Reference], []];

        Assert.Empty(Plan(Flow(code, stacks), thisIsNonNull: false));
    }

    // The slot's one store proves reads in the blocks it dominates and no others.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASlotStoredOnceProvesTheReadsItsBlockDominates(bool storeFirst)
    {
        // Dominating: newobj; stloc.0; ldarg.1; brfalse 6; ldloc.0; ldfld; pop; ret
        // Not: ldarg.1; brfalse 4; newobj; stloc.0; ldloc.0; ldfld; pop; ret
        (CilOperation, CilOperand)[] code = storeFirst
            ? [(CilOperation.NewObject, new CilOperand.MethodInstance(Constructor)), (CilOperation.StoreLocal, new CilOperand.Index(0)),
                (CilOperation.LoadArgument, new CilOperand.Index(1)), (CilOperation.BranchIfFalse, new CilOperand.BranchTarget(7)),
                (CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.LoadField, Field), (CilOperation.Pop, None),
                (CilOperation.Return, None)]
            : [(CilOperation.LoadArgument, new CilOperand.Index(1)), (CilOperation.BranchIfFalse, new CilOperand.BranchTarget(4)),
                (CilOperation.NewObject, new CilOperand.MethodInstance(Constructor)), (CilOperation.StoreLocal, new CilOperand.Index(0)),
                (CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.LoadField, Field), (CilOperation.Pop, None),
                (CilOperation.Return, None)];
        ImmutableArray<CliValueKind>[] stacks = storeFirst
            ? [[], [Reference], [], [Integer], [], [Reference], [Integer], []]
            : [[], [Integer], [], [Reference], [], [Reference], [Integer], []];

        Assert.Equal(storeFirst, Plan(Flow(code, stacks), thisIsNonNull: false).Contains(5));
    }

    // Without an entry stack, or across an instruction it does not follow, the
    // planner no longer knows what the stack holds.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownStacksProveNothing(bool missingStack)
    {
        // newobj; dup; ldvirtftn (unfollowed) or ldc.i4; pop; ldc.i4 1; stfld; ret
        var flow = Flow([(CilOperation.NewObject, new CilOperand.MethodInstance(Constructor)), (CilOperation.Duplicate, None),
            missingStack ? (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)) : (CilOperation.LoadVirtualFunction, new CilOperand.MethodInstance(Set)),
            (CilOperation.Pop, None), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.StoreField, Field),
            (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Reference], [Reference, Reference], [Reference, Reference, missingStack ? Integer : CliValueKind.NativeInt],
                [Reference, Reference], [Reference, Reference, Integer], [Reference], []],
            omitted: missingStack ? 2 : null);

        Assert.DoesNotContain(5, Plan(flow, thisIsNonNull: false));
    }

    [Fact]
    public void AMethodWithExceptionHandlersIsLeftAlone()
    {
        var flow = Flow([(CilOperation.NewObject, new CilOperand.MethodInstance(Constructor)), (CilOperation.LoadField, Field),
            (CilOperation.Pop, None), (CilOperation.Leave, new CilOperand.BranchTarget(5)), (CilOperation.EndFinally, None),
            (CilOperation.Return, None)], [[], [Reference], [Integer], [], [], []],
            regions: [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 4, 4, 1, null, null)]);

        Assert.Empty(Plan(flow, thisIsNonNull: false));
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        var calls = new Calls();
        Assert.Throws<ArgumentNullException>(() => new ConstructedReceiverPlanner(null!, new StackEffectResolver(calls), calls));
        Assert.Throws<ArgumentNullException>(() => new ConstructedReceiverPlanner(new ControlFlowDominanceAnalyzer(), null!, calls));
        Assert.Throws<ArgumentNullException>(() =>
            new ConstructedReceiverPlanner(new ControlFlowDominanceAnalyzer(), new StackEffectResolver(calls), null!));
        Assert.Throws<ArgumentNullException>(() => Planner().Plan(null!, thisIsNonNull: false));
    }

    internal static MethodInstanceModel Method(
        int token, string name, CliTypeIdentity type, CliValueKind returns, params CliValueKind[] parameters)
    {
        var signature = MethodSignatureModel.Create(returns, parameters);
        return new(new MethodDefinitionModel(new EntityKey(Assembly, token), new EntityKey(Assembly, 0x02000001), name,
            false, signature, 1), type, [], signature);
    }

    private static ImmutableHashSet<int> Plan(ValidatedControlFlowGraph flow, bool thisIsNonNull) =>
        Planner().Plan(flow, thisIsNonNull);

    private static ConstructedReceiverPlanner Planner()
    {
        var calls = new Calls();
        return new(new ControlFlowDominanceAnalyzer(), new StackEffectResolver(calls), calls);
    }

    private static ValidatedControlFlowGraph Flow(
        (CilOperation Operation, CilOperand Operand)[] code,
        ImmutableArray<CliValueKind>[] stacks,
        int? omitted = null,
        ImmutableArray<CilExceptionRegion> regions = default)
    {
        var body = new CilMethodBody(Owner.Definition, 4, [Reference],
            [.. code.Select((instruction, offset) => new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))])
        {
            MethodInstance = Owner,
            ExceptionRegions = regions.IsDefault ? [] : regions,
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        return new ValidatedControlFlowGraph(graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty),
            stacks.Select((stack, offset) => (stack, offset)).Where(entry => entry.offset != omitted)
                .ToImmutableDictionary(entry => entry.offset, entry => entry.stack));
    }

    // Resolves each method operand to its method instance.
    private sealed class Calls : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operation is CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject
                ? (instruction.Operand as CilOperand.MethodInstance)?.Value
                : null;
    }
}
