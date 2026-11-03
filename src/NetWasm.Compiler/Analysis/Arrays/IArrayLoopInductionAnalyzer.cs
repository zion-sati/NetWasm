using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

// A counted loop `for (index = c; index + Lookahead < bound; index++)` whose
// bound is an array's length, ArrayLocal's, or an int local the loop leaves
// alone, LengthLocal, as a split span's length is; the other is -1.
internal sealed record ArrayLoopInduction(
    int IndexLocal,
    int ArrayLocal,
    int GuardBlock,
    int BodyEntryBlock,
    int IncrementOffset,
    ImmutableHashSet<int> LoopBlocks)
{
    // The guard tests index + Lookahead against the length, so every element
    // from index to index + Lookahead is in bounds.
    public int Lookahead { get; init; }

    public int LengthLocal { get; init; } = -1;
}

internal interface IArrayLoopInductionAnalyzer
{
    ImmutableArray<ArrayLoopInduction> Analyze(ValidatedControlFlowGraph controlFlow);
}
