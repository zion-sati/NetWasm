// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The planner over in-memory bodies: which reads and writes through managed addresses need no null check.
public sealed class ByRefNullCheckPlannerTests
{
    private const CliValueKind Address = CliValueKind.ManagedAddress;
    private const CliValueKind Reference = CliValueKind.ManagedReference;
    private const CliValueKind Integer = CliValueKind.I4;

    private static readonly AssemblyIdentity Assembly = new("ByRefNullCheckUnitTests");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", Integer);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Test", "Pair", isValueType: true);
    private static readonly MethodInstanceModel Owner = Method(0x06000001, "Run", CliValueKind.Void);
    private static readonly MethodInstanceModel Add = Method(0x06000002, "Add", Address, Address, Integer);
    private static readonly MethodInstanceModel DataReference = Method(0x06000003, "GetArrayDataReference", Address, Reference);
    private static readonly MethodInstanceModel Read = Method(0x06000004, "Read", Address, Address);
    private static readonly CilOperand Field = new CilOperand.Entity(new EntityKey(Assembly, 0x04000001));
    private static readonly CilOperand Value = new CilOperand.TypeIdentity(Int32);
    private static readonly CilOperand None = new CilOperand.None();

    // The address of a local, argument, static, array element or field, or of a box's value, is never null.
    public static TheoryData<(CilOperation, CilOperand)[], ImmutableArray<CliValueKind>[], int> Sources() => new()
    {
        // ldloca.0; ldobj; pop; ret
        { [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Integer], []], 1 },
        // ldarga.1; ldobj; pop; ret
        { [(CilOperation.LoadArgumentAddress, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Integer], []], 1 },
        // ldsflda; ldobj; pop; ret
        { [(CilOperation.LoadStaticFieldAddress, Field), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Integer], []], 1 },
        // ldarg.0; ldc.i4 0; ldelema; ldobj; pop; ret
        { [(CilOperation.LoadArgument, Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            (CilOperation.LoadArrayElementAddress, Value), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Reference], [Reference, Integer], [Address], [Integer], []], 3 },
        // ldarg.0; ldflda; ldobj; pop; ret
        { [(CilOperation.LoadArgument, Index(0)), (CilOperation.LoadFieldAddress, Field), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.Return, None)], [[], [Reference], [Address], [Integer], []], 2 },
        // ldarg.0; unbox; ldobj; pop; ret
        { [(CilOperation.LoadArgument, Index(0)), (CilOperation.Unbox, Value), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.Return, None)], [[], [Reference], [Address], [Integer], []], 2 },
        // ldarg.0; call GetArrayDataReference; ldobj; pop; ret
        { [(CilOperation.LoadArgument, Index(0)), (CilOperation.Call, new CilOperand.MethodInstance(DataReference)), (CilOperation.LoadObject, Value),
            (CilOperation.Pop, None), (CilOperation.Return, None)], [[], [Reference], [Address], [Integer], []], 2 },
        // ldloca.0; dup; pop; ldobj; pop; ret
        { [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.Duplicate, None), (CilOperation.Pop, None), (CilOperation.LoadObject, Value),
            (CilOperation.Pop, None), (CilOperation.Return, None)], [[], [Address], [Address, Address], [Address], [Integer], []], 3 },
        // ldloca.0; ldarg.1; brfalse 3; ldobj; pop; ret: a non-null address the stack carries into the next block
        { [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.LoadArgument, Index(1)),
            (CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.Return, None)], [[], [Address], [Address, Integer], [Address], [Integer], []], 3 },
        // ldarg.1; stloc.0; ldloc.0; ldobj; pop; ldloc.0; ldobj; pop; ret: the second access through a local the block
        // stored, which the first showed non-null
        { [(CilOperation.LoadArgument, Index(1)), (CilOperation.StoreLocal, Index(0)), (CilOperation.LoadLocal, Index(0)),
            (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.LoadLocal, Index(0)), (CilOperation.LoadObject, Value),
            (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [], [Address], [Integer], [], [Address], [Integer], []], 6 },
        // ldloca.1; stloc.0; ldloc.0; ldobj; pop; ret: a local the block stored a non-null address to
        { [(CilOperation.LoadLocalAddress, Index(1)), (CilOperation.StoreLocal, Index(0)), (CilOperation.LoadLocal, Index(0)),
            (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [], [Address], [Integer], []], 3 },
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public void AddressesThatCannotBeNullAreProven((CilOperation, CilOperand)[] code, ImmutableArray<CliValueKind>[] stacks, int access) =>
        Assert.Equal([access], Plan(Flow(code, stacks)));

    // An argument's address, a call's and Unsafe's offsetting of one, may be null, as may an offset from a local's
    // address (an access throws where the address it computes is 0, which an offset can reach); an unmanaged pointer
    // and an object reference are not this planner's to prove.
    public static TheoryData<(CilOperation, CilOperand)[], ImmutableArray<CliValueKind>[]> Unknowns() => new()
    {
        // ldarg.1; ldobj; pop; ret
        { [(CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Integer], []] },
        // ldloca.0; ldc.i4 1; call Unsafe.Add; ldobj; pop; ret: an offset from a local's address
        { [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            (CilOperation.Call, new CilOperand.MethodInstance(Add)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Address, Integer], [Address], [Integer], []] },
        // ldarg.1; unaligned. 1; ldobj; pop; ldarg.1; ldobj; pop; ret: an unaligned access is not checked, so it proves
        // nothing about the plain access after it
        { [(CilOperation.LoadArgument, Index(1)), (CilOperation.Unaligned, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Address], [Integer], [], [Address], [Integer], []] },
        // ldarg.1; stloc.0; ldloc.0; ldobj; pop; ldarg.2; stloc.0; ldloc.0; ldobj; pop; ret: the local stored again
        // between the accesses, after a first access the planner cannot prove either
        { [(CilOperation.LoadArgument, Index(1)), (CilOperation.StoreLocal, Index(0)), (CilOperation.LoadLocal, Index(0)),
            (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.LoadArgument, Index(2)), (CilOperation.StoreLocal, Index(0)),
            (CilOperation.LoadLocal, Index(0)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [], [Address], [Integer], [], [Address], [], [Address], [Integer], []] },
        // ldarg.1; call Read; ldobj; pop; ret: a call that is not an intrinsic
        { [(CilOperation.LoadArgument, Index(1)), (CilOperation.Call, new CilOperand.MethodInstance(Read)), (CilOperation.LoadObject, Value),
            (CilOperation.Pop, None), (CilOperation.Return, None)], [[], [Address], [Address], [Integer], []] },
        // ldarg.1; ldc.i4 1; call Unsafe.Add; ldobj; pop; ret
        { [(CilOperation.LoadArgument, Index(1)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            (CilOperation.Call, new CilOperand.MethodInstance(Add)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Address, Integer], [Address], [Integer], []] },
        // ldarga.1; pop; ldarg.1; ldobj; pop; ldarg.1; ldobj; pop; ret: an argument exposed by address is not followed
        { [(CilOperation.LoadArgumentAddress, Index(1)), (CilOperation.Pop, None), (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value),
            (CilOperation.Pop, None), (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.Return, None)], [[], [Address], [], [Address], [Integer], [], [Address], [Integer], []] },
        // ldarg.1; ldarg.2; starg.1; ldobj; pop; ret: the value loaded before the store is the old one
        { [(CilOperation.LoadArgument, Index(1)), (CilOperation.LoadArgument, Index(2)), (CilOperation.StoreArgument, Index(1)),
            (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Address, Address], [Address], [Integer], []] },
        // ldarg.3; ldobj; pop; ret: an unmanaged pointer
        { [(CilOperation.LoadArgument, Index(3)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [CliValueKind.NativeInt], [Integer], []] },
        // ldarg.0; ldfld; pop; ret: an object reference
        { [(CilOperation.LoadArgument, Index(0)), (CilOperation.LoadField, Field), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Reference], [Integer], []] },
        // ldloca.0; ldarg.1; cpobj; ret: one of two addresses may be null
        { [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.LoadArgument, Index(1)), (CilOperation.CopyObject, Value), (CilOperation.Return, None)],
            [[], [Address], [Address, Address], []] },
    };

    [Theory]
    [MemberData(nameof(Unknowns))]
    public void AddressesThatMayBeNullAreNotProven((CilOperation, CilOperand)[] code, ImmutableArray<CliValueKind>[] stacks) =>
        Assert.Empty(Plan(Flow(code, stacks)));

    // Every address an instruction dereferences: both of cpobj's, and the receiver of a value type's field.
    [Theory]
    [InlineData(CilOperation.CopyObject)]
    [InlineData(CilOperation.StoreObject)]
    [InlineData(CilOperation.StoreField)]
    [InlineData(CilOperation.InitializeObject)]
    [InlineData(CilOperation.LoadField)]
    public void EachDereferencingInstructionIsProvenWithItsAddresses(CilOperation operation)
    {
        // ldloca.0; ldloca.1 or ldc.i4 1 (as the instruction takes); the instruction; ret
        (CilOperation, CilOperand)[] code = operation switch
        {
            CilOperation.CopyObject => [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.LoadLocalAddress, Index(1)), (operation, Value),
                (CilOperation.Return, None)],
            CilOperation.StoreObject => [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                (operation, Value), (CilOperation.Return, None)],
            CilOperation.StoreField => [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                (operation, Field), (CilOperation.Return, None)],
            CilOperation.InitializeObject => [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.Nop, None), (operation, Value),
                (CilOperation.Return, None)],
            _ => [(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.Nop, None), (operation, Field), (CilOperation.Pop, None),
                (CilOperation.Return, None)],
        };
        ImmutableArray<CliValueKind>[] stacks = operation switch
        {
            CilOperation.CopyObject => [[], [Address], [Address, Address], []],
            CilOperation.StoreObject or CilOperation.StoreField => [[], [Address], [Address, Integer], []],
            CilOperation.InitializeObject => [[], [Address], [Address], []],
            _ => [[], [Address], [Address], [Integer], []],
        };

        Assert.Equal([2], Plan(Flow(code, stacks)));
    }

    // Past a dereference the argument it read is non-null: later in the block, and in the blocks that block dominates
    // when nothing stores the argument.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ADereferencedArgumentIsProvenWhereTheDereferenceDominates(bool stored, bool proven)
    {
        // 0 ldarg.1; 1 ldobj; 2 pop; 3 ldarg.1; 4 ldobj; 5 pop; 6 ldarg.2; 7 brfalse 13; 8 ldarg.1; 9 ldobj; 10 pop;
        // 11 (ldarg.1; starg.1 or nop; nop); 13 ret
        var code = new (CilOperation, CilOperand)[]
        {
            (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.LoadArgument, Index(2)), (CilOperation.BranchIfFalse, new CilOperand.BranchTarget(13)),
            (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            stored ? (CilOperation.LoadArgument, Index(1)) : (CilOperation.Nop, None),
            stored ? (CilOperation.StoreArgument, Index(1)) : (CilOperation.Nop, None),
            (CilOperation.Return, None),
        };
        ImmutableArray<CliValueKind>[] stacks =
        [
            [], [Address], [Integer], [], [Address], [Integer], [], [Integer], [], [Address], [Integer], [],
            stored ? [Address] : [], [],
        ];

        var plan = Plan(Flow(code, stacks));

        Assert.DoesNotContain(1, plan);
        Assert.Contains(4, plan);
        Assert.Equal(proven, plan.Contains(9));
    }

    // A local stored once, with a non-null address, in a block that dominates the read holds one there.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ALocalStoredOnceWithANonNullAddressIsProvenWhereTheStoreDominates(bool nonNull, bool proven)
    {
        // 0 ldloca.1 or ldarg.1; 1 stloc.0; 2 ldarg.2; 3 brfalse 7; 4 ldloc.0; 5 ldobj; 6 pop; 7 ret
        var code = new (CilOperation, CilOperand)[]
        {
            nonNull ? (CilOperation.LoadLocalAddress, Index(1)) : (CilOperation.LoadArgument, Index(1)), (CilOperation.StoreLocal, Index(0)),
            (CilOperation.LoadArgument, Index(2)), (CilOperation.BranchIfFalse, new CilOperand.BranchTarget(7)),
            (CilOperation.LoadLocal, Index(0)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None),
        };

        Assert.Equal(proven, Plan(Flow(code, [[], [Address], [], [Integer], [], [Address], [Integer], []])).Contains(5));
    }

    // Without an entry stack, or across an instruction it does not follow, the planner no longer knows what the stack
    // holds.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownStacksProveNothing(bool missingStack)
    {
        // ldloca.0; dup; ldc.i4 0 (or ldvirtftn, unfollowed); pop; ldobj; pop; ret
        var flow = Flow([(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.Duplicate, None),
            missingStack ? (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)) : (CilOperation.LoadVirtualFunction, new CilOperand.MethodInstance(Read)),
            (CilOperation.Pop, None), (CilOperation.LoadObject, Value), (CilOperation.Pop, None), (CilOperation.Return, None)],
            [[], [Address], [Address, Address], [Address, missingStack ? Address : CliValueKind.NativeInt], [Address], [Integer], []],
            omitted: missingStack ? 4 : null);

        Assert.DoesNotContain(4, Plan(flow));
    }

    // In a method with exception handlers an access through a local's own address is proven, as in one without.
    [Fact]
    public void AMethodWithExceptionHandlersProvesAnInstructionsOwnAddress()
    {
        var flow = Flow([(CilOperation.LoadLocalAddress, Index(0)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.Leave, new CilOperand.BranchTarget(5)), (CilOperation.EndFinally, None), (CilOperation.Return, None)],
            [[], [Address], [Integer], [], [], []],
            regions: [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 4, 4, 1, null, null)]);

        Assert.Equal([1], Plan(flow));
    }

    // A dereference proves an argument in the blocks its block dominates, inside a try too, but not in the try's handler,
    // which control enters from wherever the try throws: the handler's first block is a root of the dominance even when
    // a loop in the handler branches back to it.
    [Fact]
    public void AMethodWithExceptionHandlersProvesADereferenceWhereItsBlockDominatesButNotInAHandler()
    {
        // try { 0 ldarg.1; 1 ldobj; 2 pop; 3 ldarg.2; 4 brfalse 8; 5 ldarg.1; 6 ldobj; 7 pop; 8 leave 15 }
        // finally { 9 ldarg.1; 10 ldobj; 11 pop; 12 ldarg.2; 13 brtrue 9; 14 endfinally } 15 ret
        var code = new (CilOperation, CilOperand)[]
        {
            (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.LoadArgument, Index(2)), (CilOperation.BranchIfFalse, new CilOperand.BranchTarget(8)),
            (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.Leave, new CilOperand.BranchTarget(15)),
            (CilOperation.LoadArgument, Index(1)), (CilOperation.LoadObject, Value), (CilOperation.Pop, None),
            (CilOperation.LoadArgument, Index(2)), (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(9)),
            (CilOperation.EndFinally, None), (CilOperation.Return, None),
        };
        ImmutableArray<CliValueKind>[] stacks =
        [
            [], [Address], [Integer], [], [Integer], [], [Address], [Integer], [],
            [], [Address], [Integer], [], [Integer], [], [],
        ];

        var plan = Plan(Flow(code, stacks, regions: [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 9, 9, 6, null, null)]));

        Assert.DoesNotContain(1, plan);
        Assert.Contains(6, plan);
        Assert.DoesNotContain(10, plan);
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        var calls = new Calls();
        var dominance = new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder());
        var effects = new StackEffectResolver(calls);
        var intrinsics = new Intrinsics();
        Assert.Throws<ArgumentNullException>(() => new ByRefNullCheckPlanner(null!, effects, calls, intrinsics));
        Assert.Throws<ArgumentNullException>(() => new ByRefNullCheckPlanner(dominance, null!, calls, intrinsics));
        Assert.Throws<ArgumentNullException>(() => new ByRefNullCheckPlanner(dominance, effects, null!, intrinsics));
        Assert.Throws<ArgumentNullException>(() => new ByRefNullCheckPlanner(dominance, effects, calls, null!));
        Assert.Throws<ArgumentNullException>(() => Planner().Plan(null!));
    }

    private static CilOperand.Index Index(int value) => new(value);

    private static MethodInstanceModel Method(int token, string name, CliValueKind returns, params CliValueKind[] parameters)
    {
        var signature = MethodSignatureModel.Create(returns, parameters);
        return new(new MethodDefinitionModel(new EntityKey(Assembly, token), new EntityKey(Assembly, 0x02000001), name,
            true, signature, 1), Pair, [], signature);
    }

    private static ImmutableHashSet<int> Plan(ValidatedControlFlowGraph flow) => Planner().Plan(flow);

    private static ByRefNullCheckPlanner Planner()
    {
        var calls = new Calls();
        return new(new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder()), new StackEffectResolver(calls), calls, new Intrinsics());
    }

    private static ValidatedControlFlowGraph Flow(
        (CilOperation Operation, CilOperand Operand)[] code,
        ImmutableArray<CliValueKind>[] stacks,
        int? omitted = null,
        ImmutableArray<CilExceptionRegion> regions = default)
    {
        var body = new CilMethodBody(Owner.Definition, 4, [Address, Integer],
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

    // Unsafe.Add and MemoryMarshal.GetArrayDataReference; Read is an ordinary method.
    private sealed class Intrinsics : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = method == Add.Definition.Key ? RuntimeIntrinsic.UnsafeAdd
                : method == DataReference.Definition.Key ? RuntimeIntrinsic.GetArrayDataReference
                : default;
            return method == Add.Definition.Key || method == DataReference.Definition.Key;
        }
    }
}
