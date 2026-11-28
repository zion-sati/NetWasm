// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Locals of one type that never hold a value from one block into another, and never appear in the same block, share
// one local; every other local keeps its own.
public sealed class BlockLocalSlotSharerTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly EntityKey Owner = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Boolean = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.Named(Assembly, "System", "Object", isValueType: false);
    private static readonly IControlFlowGraphBuilder Graphs = new ControlFlowGraphBuilderFactory().Create();

    // int result; if (value == 0) result = 0; else if (value == 1) result = 7; ...; return result; as Debug IL
    // compiles it: each condition stores its comparison in a bool of its own and branches on it.
    [Fact]
    public void ADebugChainsConditionTemporariesShareOneLocal()
    {
        const int conditions = 50;
        var body = Body([Int32, .. Enumerable.Repeat(Boolean, conditions)], DebugChain(conditions));

        var rewritten = new BlockLocalSlotSharer(Graphs, enabled: true).Rewrite(body);

        Assert.Equal([Int32, Boolean], rewritten.LocalSignatureTypes);
        Assert.Equal([CliValueKind.I4, CliValueKind.I4], rewritten.Locals);
        Assert.Equal(body.Instructions.Length, rewritten.Instructions.Length);
        Assert.All(rewritten.Instructions.Where(IsLocal), instruction => Assert.InRange(Local(instruction), 0, 1));
        // Only the operands of the temporaries' loads and stores change.
        Assert.Equal(
            body.Instructions.Select(instruction => IsLocal(instruction) && Local(instruction) > 0
                ? instruction with { Operand = new CilOperand.Index(1) }
                : instruction),
            rewritten.Instructions);
    }

    // A local a block reads before it writes holds a value into that block: a loop's counter, a local the method reads
    // while it is still zero. Such locals keep their own, as does one whose address is taken or that is pinned.
    [Fact]
    public void ALocalLiveIntoABlockOrAddressTakenOrPinnedKeepsItsOwn()
    {
        // locals: 0 counter, 1 zero, 2 address-taken, 3 pinned, 4 and 5 temporaries.
        var code = new CilBuilder()
            .Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0)
            .Local(CilOperation.LoadLocal, 1).Op(CilOperation.Pop)
            .Local(CilOperation.LoadLocalAddress, 2).Op(CilOperation.Pop)
            .Op(CilOperation.LoadInt32, 1).Local(CilOperation.StoreLocal, 3)
            .Label("loop")
            .Op(CilOperation.LoadInt32, 2).Local(CilOperation.StoreLocal, 4).Local(CilOperation.LoadLocal, 4).Op(CilOperation.Pop)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add).Local(CilOperation.StoreLocal, 0)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 10).Branch(CilOperation.BranchIfLessThanSigned, "loop")
            .Op(CilOperation.LoadInt32, 3).Local(CilOperation.StoreLocal, 5).Local(CilOperation.LoadLocal, 5)
            .Return()
            .Build();
        var body = Body([Int32, Int32, Int32, Int32, Int32, Int32], code) with { PinnedLocals = [3] };

        var rewritten = new BlockLocalSlotSharer(Graphs, enabled: true).Rewrite(body);

        Assert.Equal(5, rewritten.Locals.Length);
        Assert.Equal([3], rewritten.PinnedLocals);
        Assert.Equal([0, 1, 2, 3, 4, 4, 0, 0, 0, 4, 4],
            rewritten.Instructions.Where(IsLocal).Select(Local));
    }

    // Locals live in one block at once take different locals; a bool and an int are different types, though both
    // are i4 on the stack; a local written twice in a block, or written in two blocks, holds nothing across them;
    // one the code never names shares with the first of its type.
    [Fact]
    public void LocalsInOneBlockOrOfDifferentTypesStayApart()
    {
        // locals: 0 and 1 ints in one block, 2 a bool, 3 an int written in two blocks, 4 an unused int,
        // 5 an object.
        var code = new CilBuilder()
            .Op(CilOperation.LoadInt32, 1).Local(CilOperation.StoreLocal, 0)
            .Op(CilOperation.LoadInt32, 2).Local(CilOperation.StoreLocal, 1)
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 1).Op(CilOperation.Add)
            .Local(CilOperation.StoreLocal, 2)
            .Local(CilOperation.LoadLocal, 2).Branch(CilOperation.BranchIfFalse, "other")
            .Op(CilOperation.LoadInt32, 3).Local(CilOperation.StoreLocal, 3)
            .Op(CilOperation.LoadInt32, 4).Local(CilOperation.StoreLocal, 3)
            .Local(CilOperation.LoadLocal, 3).Return()
            .Label("other")
            .Op(CilOperation.LoadNull).Local(CilOperation.StoreLocal, 5)
            .Op(CilOperation.LoadInt32, 5).Local(CilOperation.StoreLocal, 3).Local(CilOperation.LoadLocal, 3).Return()
            .Build();
        var body = Body([Int32, Int32, Boolean, Int32, Int32, Object], code);

        var rewritten = new BlockLocalSlotSharer(Graphs, enabled: true).Rewrite(body);

        // Local 3 takes local 0's slot in both blocks after it; local 4, named nowhere, does too.
        Assert.Equal([Int32, Int32, Boolean, Object], rewritten.LocalSignatureTypes);
        Assert.Equal([0, 1, 0, 1, 2, 2, 0, 0, 0, 3, 0, 0],
            rewritten.Instructions.Where(IsLocal).Select(Local));
    }

    // Locals that cannot share leave the body as it was, without building its graph; so do a body whose proofs
    // name its locals, and the policy when it is off.
    [Fact]
    public void ABodyWithNothingToShareIsReturnedAsItIs()
    {
        var chain = Body([Int32, .. Enumerable.Repeat(Boolean, 4)], DebugChain(4));
        var distinct = Body([Int32, Boolean], DebugChain(1));
        var inOneBlock = Body([Int32, Int32], new CilBuilder()
            .Op(CilOperation.LoadInt32, 1).Local(CilOperation.StoreLocal, 0)
            .Op(CilOperation.LoadInt32, 2).Local(CilOperation.StoreLocal, 1)
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 1).Op(CilOperation.Add).Return()
            .Build());
        var sharer = new BlockLocalSlotSharer(Graphs, enabled: true);

        Assert.Same(distinct, new BlockLocalSlotSharer(new UnusedGraphs(), enabled: true).Rewrite(distinct));
        Assert.Same(inOneBlock, sharer.Rewrite(inOneBlock));
        Assert.Same(chain, new BlockLocalSlotSharer(new UnusedGraphs(), enabled: false).Rewrite(chain));
        var bounds = chain with { ArrayBoundsProof = new(chain.Instructions, chain.Locals, chain.LocalSignatureTypes, []) };
        Assert.Same(bounds, sharer.Rewrite(bounds));
        var checks = chain with { IntegerCheckProof = new(chain.Instructions, [], []) };
        Assert.Same(checks, sharer.Rewrite(checks));
        Assert.Throws<ArgumentNullException>(() => sharer.Rewrite(null!));
        Assert.Throws<ArgumentNullException>(() => new BlockLocalSlotSharer(null!, enabled: true));
    }

    private static ImmutableArray<CilInstruction> DebugChain(int conditions)
    {
        var code = new CilBuilder();
        for (var condition = 0; condition < conditions; condition++)
        {
            code.Label($"c{condition}")
                .Local(CilOperation.LoadArgument, 0).Op(CilOperation.LoadInt32, condition).Op(CilOperation.CompareEqual)
                .Local(CilOperation.StoreLocal, condition + 1).Local(CilOperation.LoadLocal, condition + 1)
                .Branch(CilOperation.BranchIfFalse, condition + 1 == conditions ? "else" : $"c{condition + 1}")
                .Op(CilOperation.LoadInt32, condition * 7).Local(CilOperation.StoreLocal, 0)
                .Branch(CilOperation.Branch, "join");
        }
        return code.Label("else").Op(CilOperation.LoadInt32, -1).Local(CilOperation.StoreLocal, 0)
            .Label("join").Local(CilOperation.LoadLocal, 0).Return()
            .Build();
    }

    private static bool IsLocal(CilInstruction instruction) =>
        instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress;

    private static int Local(CilInstruction instruction) => ((CilOperand.Index)instruction.Operand).Value;

    private static CilMethodBody Body(ImmutableArray<CliTypeIdentity> locals, ImmutableArray<CilInstruction> code)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), Owner, "Run", true,
            new MethodSignatureModel(Int32, [Int32]), 1);
        return new(method, 4, [.. locals.Select(local => local.StackKind)], code) { LocalSignatureTypes = locals };
    }

    private sealed class UnusedGraphs : IControlFlowGraphBuilder
    {
        public ControlFlowGraph Build(CilMethodBody methodBody) => throw new InvalidOperationException("No graph is needed.");
    }
}
