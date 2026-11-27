// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

// Where a loop's range, from a backward branch's target to its last backward branch, is entered from outside.
public sealed class CilLoopHeadsTests
{
    private static readonly AssemblyIdentity Assembly = new("loop-heads");

    // Roslyn's for and while: a branch over the body to the condition, which branches back to the body.
    [Fact]
    public void ALoopEnteredAtItsConditionHasItsHeadThere()
    {
        var body = Body(
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),
            (CilOperation.Branch, new CilOperand.BranchTarget(5)),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
            (CilOperation.Return, new CilOperand.None()));

        Assert.Equal(5, CilLoopHeads.Head(body, 3, 6));
    }

    // A do-while: control falls into the body's start.
    [Fact]
    public void ALoopFallenIntoHasItsHeadAtItsStart()
    {
        var body = Body(
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
            (CilOperation.Return, new CilOperand.None()));

        Assert.Equal(1, CilLoopHeads.Head(body, 1, 3));
        Assert.Equal(0, CilLoopHeads.Head(body, 0, 3));
    }

    // Entered at two places, the loop is irreducible already: its start stays the head.
    [Fact]
    public void ALoopEnteredTwiceKeepsItsStart()
    {
        var body = Body(
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(2)),
            (CilOperation.Return, new CilOperand.None()));

        Assert.Equal(2, CilLoopHeads.Head(body, 2, 5));
    }

    // A switch entering the range counts as an entry like a branch does.
    [Fact]
    public void ASwitchIntoTheLoopIsAnEntry()
    {
        var body = Body(
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.Switch, new CilOperand.SwitchTargets([5])),
            (CilOperation.Branch, new CilOperand.BranchTarget(5)),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
            (CilOperation.Return, new CilOperand.None()));
        var twice = Body(
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.Switch, new CilOperand.SwitchTargets([4])),
            (CilOperation.Branch, new CilOperand.BranchTarget(5)),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
            (CilOperation.Return, new CilOperand.None()));

        Assert.Equal(5, CilLoopHeads.Head(body, 3, 6));
        Assert.Equal(3, CilLoopHeads.Head(twice, 3, 6));
    }

    // A handler inside the range whose protected block is outside it is entered from outside; the dispatch never
    // enters a handler, so the range keeps its start.
    [Fact]
    public void ARangeHoldingAHandlerOfAnOutsideBlockKeepsItsStart()
    {
        var body = Body(
            (CilOperation.Nop, new CilOperand.None()),
            (CilOperation.Leave, new CilOperand.BranchTarget(6)),
            (CilOperation.Branch, new CilOperand.BranchTarget(5)),
            (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.Leave, new CilOperand.BranchTarget(6)),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
            (CilOperation.Return, new CilOperand.None())) with
        {
            ExceptionRegions = [new CilExceptionRegion(CilExceptionRegionKind.Catch, 0, 2, 3, 2, null, null)],
        };

        Assert.Equal(3, CilLoopHeads.Head(body, 3, 6));
    }

    private static CilMethodBody Body(params (CilOperation Operation, CilOperand Operand)[] instructions)
    {
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(CliTypeIdentity.Primitive("void", CliValueKind.Void), []), 1);
        return new CilMethodBody(definition, 4, [CliValueKind.I4],
            [.. instructions.Select((instruction, offset) =>
                new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))]);
    }
}
