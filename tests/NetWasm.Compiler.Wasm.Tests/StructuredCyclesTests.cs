// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// The offsets of the blocks on a cycle of ordinary edges, a block's exit included: none in a diamond; both blocks of a
// loop through two; a block that branches to itself.
public sealed class StructuredCyclesTests
{
    [Fact]
    public void ADiamondHasNoCycle()
    {
        var condition = new StructuredCondition(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null);
        var method = Method(
            Block(0, [I(0, CilOperation.LoadInt32)], new StructuredConditionalExit(condition, new(1), new(2))),
            Block(1, [I(1, CilOperation.Call)], new StructuredFallthroughExit(new(3))),
            Block(2, [I(2, CilOperation.Call)], new StructuredFallthroughExit(new(3))),
            Block(3, [], new StructuredTerminalExit(I(3, CilOperation.Return))));

        Assert.Empty(StructuredCycles.Offsets(method));
    }

    [Fact]
    public void BothBlocksOfALoopAndTheirExitsAreOnACycle()
    {
        var condition = new StructuredCondition(2, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null);
        var method = Method(
            Block(0, [I(0, CilOperation.Nop)], new StructuredFallthroughExit(new(1))),
            Block(1, [I(1, CilOperation.Call)], new StructuredConditionalExit(condition, new(2), new(3))),
            Block(2, [I(3, CilOperation.Call)], new StructuredBranchExit(4, new(1))),
            Block(3, [], new StructuredTerminalExit(I(5, CilOperation.Return))));

        Assert.Equal([1, 2, 3, 4], StructuredCycles.Offsets(method).Order());
    }

    [Fact]
    public void ABlockThatBranchesToItselfIsOnACycle()
    {
        var method = Method(
            Block(0, [I(0, CilOperation.Nop)], new StructuredFallthroughExit(new(1))),
            Block(1, [I(1, CilOperation.Call)], new StructuredBranchExit(2, new(1))));

        Assert.Equal([1, 2], StructuredCycles.Offsets(method).Order());
    }

    [Fact]
    public void RejectsAMissingMethod() => Assert.Throws<ArgumentNullException>(() => StructuredCycles.Offsets(null!));

    private static StructuredBlockDefinition Block(int id, ImmutableArray<CilInstruction> instructions, StructuredBlockExit exit) =>
        new(new(id), id, instructions, [], exit) { EndOffset = id + instructions.Length };

    private static StructuredMethod Method(params StructuredBlockDefinition[] blocks)
    {
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1);
        return new(new(definition, null, 1, [], [], []), new(0),
            blocks.ToImmutableDictionary(static block => block.Id), StructuredSequence.Empty, [], [], []);
    }
}
