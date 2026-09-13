using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

public sealed record BasicBlock(
    int Index,
    int StartOffset,
    ImmutableArray<CilInstruction> Instructions)
{
    public CilInstruction Terminator => Instructions[^1];
}

public sealed record ControlFlowGraph
{
    private readonly ImmutableDictionary<int, BasicBlock> _blocksByOffset;

    internal ControlFlowGraph(
        CilMethodBody methodBody,
        ImmutableArray<BasicBlock> blocks,
        ImmutableDictionary<int, ImmutableArray<int>> successors,
        ImmutableDictionary<int, ImmutableArray<int>> predecessors,
        ImmutableDictionary<int, ImmutableArray<int>> exceptionalSuccessors,
        ImmutableDictionary<int, ImmutableArray<int>> exceptionalPredecessors,
        ImmutableHashSet<int> reachableBlocks)
    {
        MethodBody = methodBody;
        Blocks = blocks;
        Successors = successors;
        Predecessors = predecessors;
        ExceptionalSuccessors = exceptionalSuccessors;
        ExceptionalPredecessors = exceptionalPredecessors;
        ReachableBlocks = reachableBlocks;
        _blocksByOffset = blocks.ToImmutableDictionary(
            block => block.StartOffset,
            block => block);
    }

    public CilMethodBody MethodBody { get; }
    public ImmutableArray<BasicBlock> Blocks { get; }
    public ImmutableDictionary<int, ImmutableArray<int>> Successors { get; }
    public ImmutableDictionary<int, ImmutableArray<int>> Predecessors { get; }
    public ImmutableDictionary<int, ImmutableArray<int>> ExceptionalSuccessors { get; }
    public ImmutableDictionary<int, ImmutableArray<int>> ExceptionalPredecessors { get; }
    public ImmutableHashSet<int> ReachableBlocks { get; }
    public BasicBlock Entry => Blocks[0];

    public BasicBlock GetBlock(int index) => Blocks[index];
    public BasicBlock GetBlockAtOffset(int offset) => _blocksByOffset[offset];
}
