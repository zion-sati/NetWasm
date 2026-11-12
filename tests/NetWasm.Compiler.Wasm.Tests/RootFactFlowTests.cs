using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// The forward must-analysis of root-frame facts: a block some path reaches gets the meet of its reached
// predecessors' exits, and a block no path reaches, as a cycle no edge enters, the empty fact.
public sealed class RootFactFlowTests
{
    [Fact]
    public void ACycleNoPathReachesGetsTheEmptyFact()
    {
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Nop)], new StructuredTerminalExit(I(1, CilOperation.Return))),
            BlockMethods.Block(1, [I(2, CilOperation.Nop)], new StructuredBranchExit(4, new(2))),
            BlockMethods.Block(2, [I(4, CilOperation.Nop)], new StructuredBranchExit(2, new(1))));
        var flow = new StructuredBlockPredecessorReader().Read(method);

        var entries = RootFactFlow.Solve(method, flow, 0, Math.Min, (left, right) => left == right,
            (block, fact) => fact + block.Instructions.Length);

        Assert.DoesNotContain(new StructuredBlockId(1), flow.Opaque);
        Assert.Equal([0, 0, 0], entries.OrderBy(pair => pair.Key.Value).Select(pair => pair.Value));
    }

    // A loop meets its back edge until the facts stop changing.
    [Fact]
    public void ALoopMeetsItsBackEdgeUntilTheFactsSettle()
    {
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Nop)], new StructuredFallthroughExit(new(1))),
            BlockMethods.Block(1, [I(1, CilOperation.Nop)], new StructuredConditionalExit(
                new(2, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
            BlockMethods.Block(2, [I(3, CilOperation.Nop)], new StructuredTerminalExit(I(4, CilOperation.Return))));
        var flow = new StructuredBlockPredecessorReader().Read(method);

        var entries = RootFactFlow.Solve(method, flow, 0, Math.Min, (left, right) => left == right,
            (_, fact) => Math.Min(fact + 1, 3));

        Assert.Equal([0, 1, 2], entries.OrderBy(pair => pair.Key.Value).Select(pair => pair.Value));
    }
}
