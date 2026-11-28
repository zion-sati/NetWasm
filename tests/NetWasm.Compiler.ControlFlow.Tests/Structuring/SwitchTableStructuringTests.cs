// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;
using static NetWasm.Compiler.ControlFlow.Tests.Structuring.StructuredTree;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// A CIL switch, decoded into a chain of compares, is structured as one table when it has 16 cases or more and every
// target is a region of its own: a labeled block per distinct target around the table, each followed by its target's
// code. Otherwise the chain stays a chain.
public sealed class SwitchTableStructuringTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(40)]
    public void ASwitchOfSixteenCasesOrMoreIsOneTable(int cases)
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, cases).Select(index => $"t{index}")]);
        Return(il, -1);
        for (var index = 0; index < cases; index++)
        {
            il.Label($"t{index}");
            Return(il, index * 7);
        }

        var method = Structurize(il.Body());

        var table = Assert.Single(Descendants(method.Body).OfType<StructuredSwitch>());
        Assert.Equal(cases, table.Cases.Length);
        Assert.Equal(cases, table.Cases.Distinct().Count());
        // A label per case and the default; every case returns, so there is no join.
        Assert.Equal(cases + 1, Descendants(method.Body).OfType<StructuredLabeledBlock>().Count());
        Assert.Empty(Descendants(method.Body).OfType<StructuredIf>());
    }

    [Fact]
    public void FifteenCasesKeepTheChain()
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 15).Select(index => $"t{index}")]);
        Return(il, -1);
        for (var index = 0; index < 15; index++)
        {
            il.Label($"t{index}");
            Return(il, index * 7);
        }

        var method = Structurize(il.Body());

        Assert.Empty(Descendants(method.Body).OfType<StructuredSwitch>());
        Assert.Equal(15, Descendants(method.Body).OfType<StructuredIf>().Count());
    }

    // Cases that assign and meet after the switch branch to one label around it; an empty case is that label.
    [Fact]
    public void CasesThatMeetBranchToALabelAroundTheTable()
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => index == 7 ? "join" : $"t{index}")]);
        Assign(il, -1, "join");
        for (var index = 0; index < 20; index++)
        {
            if (index == 7)
            {
                continue;
            }
            il.Label($"t{index}");
            Assign(il, index * 3, "join");
        }
        Join(il);

        var method = Structurize(il.Body());

        var outer = Assert.IsType<StructuredLabeledBlock>(method.Body.Regions[0]);
        var table = Assert.Single(Descendants(outer.Body).OfType<StructuredSwitch>());
        Assert.Equal(outer.Next, table.Cases[7]);
        Assert.Equal(20, Descendants(outer.Body).OfType<StructuredLabeledBlock>().Count());
        Assert.Equal(20, Descendants(outer.Body).OfType<StructuredLabelBranch>().Count());
    }

    // Cases with the same target name one label; a case may share the default's code, which leaves the chain's final
    // pop a block of its own.
    [Fact]
    public void CasesSharingATargetShareItsLabel()
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => index == 7 ? "default" : $"t{index % 4}")]);
        il.Label("default");
        Return(il, -1);
        for (var index = 0; index < 4; index++)
        {
            il.Label($"t{index}");
            Return(il, index * 7);
        }

        var method = Structurize(il.Body());

        var table = Assert.Single(Descendants(method.Body).OfType<StructuredSwitch>());
        Assert.Equal(5, table.Cases.Distinct().Count());
        Assert.Contains(table.Default, table.Cases);
        Assert.Equal(5, Descendants(method.Body).OfType<StructuredLabeledBlock>().Count());
    }

    // A wasm br_table takes an i32. The chain's compares validate only against an int32 value, so the step's check
    // holds that invariant: given a native int at the chain's head it declines.
    [Fact]
    public void ANativeIntValueKeepsTheChain()
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => $"t{index}")]);
        Return(il, -1);
        for (var index = 0; index < 20; index++)
        {
            il.Label($"t{index}");
            Return(il, index);
        }
        var validated = Validate(il.Body());
        var state = new ControlFlowStructuringState(validated with
        {
            InstructionEntryStacks = validated.InstructionEntryStacks.SetItem(il.Offset("c0"), [CliValueKind.NativeInt]),
        });

        var result = Step().Execute(new StructuredControlFlowBuilderProbe(), state, state.Graph.Entry.Index, null,
            state.Graph.ReachableBlocks, [], ImmutableArray.CreateBuilder<StructuredRegionDraft>());

        Assert.Equal(StructuredSequenceStepDisposition.NotHandled, result.Disposition);
        Assert.Empty(state.ClaimedBlockBodies);
    }

    // A chain block entered from elsewhere (a later case's compare, a case's branch, the default's pop) or a case's
    // target entered from outside the switch is not the switch's alone: the step declines.
    [Theory]
    [InlineData("c5")]
    [InlineData("s5")]
    [InlineData("final")]
    [InlineData("t9")]
    public void ABlockEnteredFromOutsideTheSwitchKeepsTheChain(string entered)
    {
        var il = new Il { MaxStack = 3 };
        // The chain's blocks are entered with the value on the stack, a case's target with an empty one.
        if (entered != "t9")
        {
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        }
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 77);
        il.Emit(CilOperation.BranchIfEqual, entered);
        il.Label("head");
        if (entered == "t9")
        {
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        }
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => $"t{index}")]);
        Return(il, -1);
        for (var index = 0; index < 20; index++)
        {
            il.Label($"t{index}");
            Return(il, index);
        }
        var state = new ControlFlowStructuringState(Validate(il.Body()));

        var result = Step().Execute(new StructuredControlFlowBuilderProbe(), state,
            state.Graph.GetBlockAtOffset(il.Offset("head")).Index, null,
            state.Graph.ReachableBlocks, [], ImmutableArray.CreateBuilder<StructuredRegionDraft>());

        Assert.Equal(StructuredSequenceStepDisposition.NotHandled, result.Disposition);
        Assert.Empty(state.ClaimedBlockBodies);
    }

    // A chain block the sequence does not own, one another step takes (here the loop's break target), one another
    // region already holds, or the block the sequence stops at leaves the chain to the branching step.
    [Theory]
    [InlineData("allowed")]
    [InlineData("reserved")]
    [InlineData("claimed")]
    [InlineData("stop")]
    public void AChainBlockTheSwitchCannotOwnKeepsTheChain(string reason)
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => $"t{index}")]);
        Return(il, -1);
        for (var index = 0; index < 20; index++)
        {
            il.Label($"t{index}");
            Return(il, index);
        }
        var state = new ControlFlowStructuringState(Validate(il.Body()));
        var block = state.Graph.GetBlockAtOffset(il.Offset(reason == "allowed" ? "s3" : "c3")).Index;
        if (reason == "reserved")
        {
            state.LoopExitTargets.Push(block);
        }
        if (reason == "claimed")
        {
            state.ClaimedBlockBodies.Add(block);
        }

        var result = Step().Execute(new StructuredControlFlowBuilderProbe(), state, state.Graph.Entry.Index,
            reason == "stop" ? block : null,
            reason == "allowed" ? state.Graph.ReachableBlocks.Remove(block) : state.Graph.ReachableBlocks, [],
            ImmutableArray.CreateBuilder<StructuredRegionDraft>());

        Assert.Equal(StructuredSequenceStepDisposition.NotHandled, result.Disposition);
    }

    // The enclosing join is the switch's when its cases meet there, though the sequence's blocks leave it out.
    [Fact]
    public void CasesMeetingAtTheEnclosingJoinBranchToIt()
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => $"t{index}")]);
        Assign(il, -1, "join");
        for (var index = 0; index < 20; index++)
        {
            il.Label($"t{index}");
            Assign(il, index, "join");
        }
        Join(il);
        var state = new ControlFlowStructuringState(Validate(il.Body()));
        var join = state.Graph.GetBlockAtOffset(il.Offset("join")).Index;
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();

        var result = Step().Execute(new StructuredControlFlowBuilderProbe(), state, state.Graph.Entry.Index, join,
            state.Graph.ReachableBlocks.Remove(join), [], regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Equal(join, result.NextBlockOffset);
        Assert.Equal(join, Assert.IsType<StructuredLabeledBlockDraft>(Assert.Single(regions)).NextBlock);
    }

    // A state machine: a loop whose header is the switch, every case continuing it but the one that returns, and a
    // default that returns. Left only from its cases, the loop is unconditional with the whole chain in its body.
    [Theory]
    [InlineData(19, false)]
    [InlineData(5, false)]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    public void AStateMachinesSwitchAtItsLoopHeaderIsOneTable(int returning, bool meetAfter)
    {
        var il = new Il { MaxStack = 3 };
        il.Label("head");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => $"t{index}")]);
        Return(il, -1);
        for (var index = 0; index < 20; index++)
        {
            il.Label($"t{index}");
            if (index == returning)
            {
                Return(il, 5);
                continue;
            }
            // The cases meet at the head, or after the switch, where the loop's code continues.
            Assign(il, (index * 7 + 3) % 20, meetAfter ? "after" : "head");
        }
        if (meetAfter)
        {
            il.Label("after");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, 1);
            il.Emit(CilOperation.Add);
            il.Emit(CilOperation.StoreArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "head");
        }

        var method = Structurize(il.Body());

        Assert.Single(Descendants(method.Body).OfType<StructuredSwitch>());
        Assert.Null(Assert.Single(Descendants(method.Body).OfType<StructuredLoop>()).Condition);
    }

    // In a loop, a case that continues branches past the code after the switch to the loop's increment, which the
    // others reach through that code: two labels, the join's inside the increment's. A case may also leave the loop
    // through code of its own. Code after the switch entered from before it keeps the chain.
    [Theory]
    [InlineData(null, true)]
    [InlineData("after", false)]
    [InlineData("away", true)]
    public void ASwitchInALoopTakesCasesThatContinueOrBreak(string? elsewhere, bool tables)
    {
        var il = new Il { MaxStack = 3 };
        il.Label("head");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 100);
        il.Emit(CilOperation.BranchIfGreaterThanOrEqualSigned, "exit");
        if (elsewhere == "after")
        {
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, 50);
            il.Emit(CilOperation.BranchIfEqual, "after");
        }
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => index switch
        {
            2 => "increment",
            5 => "exit",
            8 => "after",
            11 when elsewhere == "away" => "away",
            _ => $"t{index}",
        })]);
        Assign(il, -1, "after");
        for (var index = 0; index < 20; index++)
        {
            if (index is 2 or 5 or 8 || index == 11 && elsewhere == "away")
            {
                // Those cases branch elsewhere; an unreachable block here would enter the code after the switch.
                continue;
            }
            il.Label($"t{index}");
            Assign(il, index * 3, "after");
        }
        il.Label("after");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 3);
        il.Emit(CilOperation.Multiply);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Label("increment");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.StoreArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.Branch, "head");
        il.Label("away");
        il.Emit(CilOperation.LoadInt32, 9);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Label("exit");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Return);

        var method = Structurize(il.Body());

        Assert.Equal(tables ? 1 : 0, Descendants(method.Body).OfType<StructuredSwitch>().Count());
        if (tables)
        {
            // The increment's label, the outermost around the table, holds the join's: the code after the switch runs
            // between them.
            var outer = Descendants(method.Body).OfType<StructuredLabeledBlock>().First(labeled =>
                Descendants(labeled.Body).OfType<StructuredSwitch>().Any() &&
                labeled.Body.Regions[0] is StructuredLabeledBlock);
            Assert.NotEqual(outer.Next, ((StructuredLabeledBlock)outer.Body.Regions[0]).Next);
        }
    }

    [Fact]
    public void ConstructorRejectsNullCollaborators()
    {
        var postDominators = new ControlFlowPostDominatorFinder(new ControlFlowPostDominanceAnalyzer(new DominatorTreeBuilder()));
        Assert.Equal("postDominators", Assert.Throws<ArgumentNullException>(() =>
            new SwitchSequenceStepExecutor(null!, new SwitchChainReader(), new SwitchJoinSelector(), new SwitchTablePolicy(16, 16384))).ParamName);
        Assert.Equal("chains", Assert.Throws<ArgumentNullException>(() =>
            new SwitchSequenceStepExecutor(postDominators, null!, new SwitchJoinSelector(), new SwitchTablePolicy(16, 16384))).ParamName);
        Assert.Equal("joins", Assert.Throws<ArgumentNullException>(() =>
            new SwitchSequenceStepExecutor(postDominators, new SwitchChainReader(), null!, new SwitchTablePolicy(16, 16384))).ParamName);
        Assert.Equal("tables", Assert.Throws<ArgumentNullException>(() =>
            new SwitchSequenceStepExecutor(postDominators, new SwitchChainReader(), new SwitchJoinSelector(), null!)).ParamName);
    }

    // The step claims the chain's blocks for the switch, and the head for the boundary that owns it; a second claim
    // is not the original. A join the post-dominators put at the switch's own head is no join: the step declines.
    [Fact]
    public void TheStepClaimsTheChainAndDeclinesAJoinAtItsHead()
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => $"t{index}")]);
        Assign(il, -1, "join");
        for (var index = 0; index < 20; index++)
        {
            il.Label($"t{index}");
            Assign(il, index, "join");
        }
        Join(il);
        var state = new ControlFlowStructuringState(Validate(il.Body()));
        var head = state.Graph.Entry.Index;
        state.BoundaryOwnedBlocks.Add(head);
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var builder = new StructuredControlFlowBuilderProbe();
        var step = Step();

        var first = step.Execute(builder, state, state.Graph.Entry.Index, null, state.Graph.ReachableBlocks, [], regions);
        var second = step.Execute(builder, state, state.Graph.Entry.Index, null, state.Graph.ReachableBlocks, [], regions);
        var atHead = new SwitchSequenceStepExecutor(new HeadPostDominates(head), new SwitchChainReader(), new SwitchJoinSelector(),
            new SwitchTablePolicy(16, 16384))
            .Execute(builder, new ControlFlowStructuringState(Validate(il.Body())), state.Graph.Entry.Index, null,
                state.Graph.ReachableBlocks, [], regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, first.Disposition);
        Assert.Equal(state.Graph.GetBlockAtOffset(il.Offset("join")).Index, first.NextBlockOffset);
        Assert.Contains(head, state.BoundaryClaimedBlockBodies);
        var tables = regions.Select(region => Assert.IsType<StructuredLabeledBlockDraft>(region))
            .Select(labeled => DraftTable(labeled.Body)).ToArray();
        Assert.True(tables[0].IsOriginal);
        Assert.False(tables[1].IsOriginal);
        Assert.Equal(StructuredSequenceStepDisposition.NotHandled, atHead.Disposition);
        Assert.Equal(2, regions.Count);
    }

    // A case whose target the sequence does not own leaves it other than for the enclosing join: the step declines.
    [Fact]
    public void ATargetOutsideTheAllowedBlocksKeepsTheChain()
    {
        var il = new Il { MaxStack = 3 };
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        Switch(il, [.. Enumerable.Range(0, 20).Select(index => $"t{index}")]);
        Assign(il, -1, "join");
        for (var index = 0; index < 20; index++)
        {
            il.Label($"t{index}");
            Assign(il, index, "join");
        }
        Join(il);
        var state = new ControlFlowStructuringState(Validate(il.Body()));
        var outside = state.Graph.GetBlockAtOffset(il.Offset("t4")).Index;

        var result = Step().Execute(new StructuredControlFlowBuilderProbe(), state, state.Graph.Entry.Index,
            state.Graph.GetBlockAtOffset(il.Offset("join")).Index, state.Graph.ReachableBlocks.Remove(outside), [],
            ImmutableArray.CreateBuilder<StructuredRegionDraft>());

        Assert.Equal(StructuredSequenceStepDisposition.NotHandled, result.Disposition);
    }

    private static SwitchSequenceStepExecutor Step() => new(new ControlFlowPostDominatorFinder(
        new ControlFlowPostDominanceAnalyzer(new DominatorTreeBuilder())), new SwitchChainReader(), new SwitchJoinSelector(),
        new SwitchTablePolicy(16, 16384));

    private static StructuredSwitchDraft DraftTable(StructuredSequenceDraft sequence) =>
        sequence.Regions[0] switch
        {
            StructuredSwitchDraft table => table,
            StructuredLabeledBlockDraft labeled => DraftTable(labeled.Body),
            var other => throw new InvalidOperationException($"unexpected {other}"),
        };

    // The decoder's form of a CIL switch over the value on the stack: per case, dup, ldc.i4 k, bne.un to the next
    // case, pop, br to its target; then a pop that falls into the default's code.
    private static void Switch(Il il, IReadOnlyList<string> targets)
    {
        for (var index = 0; index < targets.Count; index++)
        {
            il.Label($"c{index}");
            il.Emit(CilOperation.Duplicate);
            il.Emit(CilOperation.LoadInt32, index);
            il.Emit(CilOperation.BranchIfNotEqual, index == targets.Count - 1 ? "final" : $"c{index + 1}");
            il.Label($"s{index}");
            il.Emit(CilOperation.Pop);
            il.Emit(CilOperation.Branch, targets[index]);
        }
        il.Label("final");
        il.Emit(CilOperation.Pop);
    }

    private static void Return(Il il, int value)
    {
        il.Emit(CilOperation.LoadInt32, value);
        il.Emit(CilOperation.Return);
    }

    private static void Assign(Il il, int value, string next)
    {
        il.Emit(CilOperation.LoadInt32, value);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Branch, next);
    }

    // The code after the switch reads the local the cases assign; too large for the structurer to copy into each.
    private static void Join(Il il)
    {
        il.Label("join");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 2);
        il.Emit(CilOperation.Multiply);
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.Return);
    }

    // Post-dominators in which the head post-dominates every other block.
    private sealed class HeadPostDominates(int head) : IControlFlowPostDominatorFinder
    {
        public DominatorTree Find(ControlFlowStructuringState state, int? stop, ImmutableHashSet<int> allowed) =>
            DominatorTree.FromImmediateDominators(allowed,
                allowed.ToDictionary(block => block, block => block == head ? (int?)null : head));
    }
}
