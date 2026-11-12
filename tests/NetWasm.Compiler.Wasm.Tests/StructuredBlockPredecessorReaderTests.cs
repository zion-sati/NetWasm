using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StructuredBlockPredecessorReaderTests
{
    private readonly IStructuredBlockPredecessorReader _reader =
        Assert.IsAssignableFrom<IStructuredBlockPredecessorReader>(new StructuredBlockPredecessorReader());

    [Fact]
    public void OrdinaryExitsAreEdgesAndTheEntryIsOpaque()
    {
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Call)], new StructuredConditionalExit(
                new(1, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
            BlockMethods.Block(1, [I(2, CilOperation.Call)], new StructuredBranchExit(3, new(3))),
            BlockMethods.Block(2, [I(4, CilOperation.Call)], new StructuredFallthroughExit(new(3))),
            BlockMethods.Block(3, [I(6, CilOperation.Call)], new StructuredFallthroughExit(new(1))));

        var flow = _reader.Read(method);

        Assert.Equal([new StructuredBlockId(0)], flow.Opaque);
        Assert.Equal([new StructuredBlockId(0), new(3)], flow.Predecessors[new(1)].OrderBy(id => id.Value));
        Assert.Equal([new StructuredBlockId(0)], flow.Predecessors[new(2)]);
        Assert.Equal([new StructuredBlockId(1), new(2)], flow.Predecessors[new(3)].OrderBy(id => id.Value));
        Assert.Empty(flow.Predecessors[new(0)]);
    }

    // A leave target may follow a finally, a handler or filter is entered by an exception, and a
    // block no ordinary edge reaches has no known predecessor: facts never reach their entries.
    [Fact]
    public void BlocksEnteredOtherThanByOrdinaryEdgesAreOpaque()
    {
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Call)], new StructuredLeaveExit(1, new(1))),
            BlockMethods.Block(1, [I(2, CilOperation.Call)], new StructuredBranchExit(3, new(9))),
            BlockMethods.Block(2, [I(4, CilOperation.Call)], new StructuredTerminalExit(I(5, CilOperation.Return))),
            BlockMethods.Block(3, [I(6, CilOperation.Call)], new StructuredTerminalExit(I(7, CilOperation.Return))),
            BlockMethods.Block(4, [I(8, CilOperation.Call)], new StructuredTerminalExit(I(9, CilOperation.Return))),
            BlockMethods.Block(5, [I(10, CilOperation.Call)], new StructuredTerminalExit(I(11, CilOperation.Return))));
        method = method with
        {
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty.Add(new(0),
                new StructuredExceptionGroup(new(0), null, 0, 2, [],
                    [
                        new StructuredExceptionClause(CilExceptionRegionKind.Filter, 4, 2, null, 6,
                            StructuredSequence.Empty, StructuredSequence.Empty)
                        {
                            HandlerBlock = new(2),
                            FilterBlock = new(3),
                        },
                    ],
                    [new(new(0), 8, new(4), StructuredSequence.Empty)], null, new(5))),
        };

        var flow = _reader.Read(method);

        Assert.Equal(
            [new StructuredBlockId(0), new(1), new(2), new(3), new(4), new(5), new(9)],
            flow.Opaque.OrderBy(id => id.Value));
        Assert.Empty(flow.Predecessors[new(1)]);
        Assert.Throws<ArgumentNullException>(() => _reader.Read(null!));
    }
}

// Methods of several blocks for the root planners' flow tests.
internal static class BlockMethods
{
    public static StructuredBlockDefinition Block(int id, ImmutableArray<CilInstruction> instructions,
        StructuredBlockExit exit) =>
        new(new(id), instructions[0].Offset, instructions, [], exit) { EndOffset = instructions[^1].Offset + 1 };

    public static StructuredMethod Of(params StructuredBlockDefinition[] blocks)
    {
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        var instructions = blocks.SelectMany(block => block.Instructions).ToImmutableArray();
        return new(new(definition, null, 1, [], [], instructions), blocks[0].Id,
            blocks.ToImmutableDictionary(block => block.Id), StructuredSequence.Empty, [], [], []);
    }
}
