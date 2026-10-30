using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed record StructuredMethodDraft(
    ValidatedControlFlowGraph ValidatedGraph,
    StructuredSequenceDraft Body,
    ImmutableArray<StructuredExceptionGroupDraft> ExceptionGroups,
    ImmutableArray<StructuredExceptionPartDraft> BodyParts);

internal sealed record StructuredExceptionGroupDraft(
    int TryOffset,
    int TryLength,
    ImmutableArray<StructuredExceptionPartDraft> ProtectedParts,
    ImmutableArray<StructuredExceptionClauseDraft> Clauses,
    ImmutableArray<StructuredExceptionContinuationDraft> NormalContinuations)
{
    public StructuredDispatcherDraft? ContinuationDispatcher { get; init; }

    public int? ContinuationJoinBlock { get; init; }
}

internal sealed record StructuredExceptionContinuationDraft(
    int TargetOffset,
    StructuredSequenceDraft Body);

internal abstract record StructuredExceptionPartDraft;

internal sealed record StructuredExceptionCodeDraft(StructuredSequenceDraft Body)
    : StructuredExceptionPartDraft;

internal sealed record StructuredNestedExceptionGroupDraft(StructuredExceptionGroupDraft Group)
    : StructuredExceptionPartDraft;

internal sealed record StructuredExceptionClauseDraft(
    CilExceptionRegion Region,
    StructuredSequenceDraft HandlerBody,
    StructuredSequenceDraft? FilterBody);

internal abstract record StructuredRegionDraft;

internal sealed record StructuredSequenceDraft(ImmutableArray<StructuredRegionDraft> Regions)
    : StructuredRegionDraft
{
    public static StructuredSequenceDraft Empty { get; } = new([]);
}

internal sealed record StructuredBlockDraft(
    BasicBlock Block,
    bool IsOriginal = true) : StructuredRegionDraft;

internal sealed record StructuredLoopBreakDraft : StructuredRegionDraft;

internal sealed record StructuredLoopContinueDraft : StructuredRegionDraft;

internal sealed record StructuredDispatcherContinueDraft(int TargetBlock) : StructuredRegionDraft;

// A branch to NextBlock inside Body leaves the block, and NextBlock's code
// follows it.
internal sealed record StructuredLabeledBlockDraft(int NextBlock, StructuredSequenceDraft Body)
    : StructuredRegionDraft;

// A branch to HeaderBlock inside Body starts Body again; Body begins with
// HeaderBlock's code and never reaches its end.
internal sealed record StructuredLabeledLoopDraft(int HeaderBlock, StructuredSequenceDraft Body)
    : StructuredRegionDraft;

// A branch to the enclosing labeled block or loop that names TargetBlock.
internal sealed record StructuredLabelBranchDraft(int TargetBlock) : StructuredRegionDraft;

internal sealed record StructuredExceptionRegionDraft(
    StructuredExceptionGroupDraft Group,
    int? FallthroughContinuationIndex) : StructuredRegionDraft
{
    public ImmutableHashSet<int> DispatcherContinuations { get; init; } = [];
}

internal sealed record StructuredDispatcherDraft(
    int? EntryBlock,
    ImmutableArray<StructuredDispatcherBlockDraft> Blocks,
    ImmutableArray<StructuredDispatcherExitDraft> Exits) : StructuredRegionDraft;

internal sealed record StructuredDispatcherBlockDraft(
    BasicBlock Block,
    int? WhenTrue,
    int? WhenFalse)
{
    public bool IsOriginal { get; init; } = true;
}

internal sealed record StructuredDispatcherExitDraft(
    int TargetBlock,
    StructuredSequenceDraft Body);

internal sealed record StructuredIfDraft(
    BasicBlock ConditionBlock,
    StructuredSequenceDraft WhenTrue,
    StructuredSequenceDraft WhenFalse) : StructuredRegionDraft
{
    public bool IsOriginal { get; init; } = true;
}

// A null ConditionBlock is an unconditional loop that leaves only through
// breaks, returns or exception-region continuations; ContinueBody and ExitBody
// stay empty.
internal sealed record StructuredLoopDraft(
    BasicBlock? ConditionBlock,
    bool ContinueWhenConditionTrue,
    StructuredSequenceDraft Body,
    StructuredSequenceDraft ContinueBody,
    StructuredSequenceDraft ExitBody) : StructuredRegionDraft
{
    public bool IsOriginal { get; init; } = true;
}

internal sealed record StructuredPostTestLoopDraft(
    StructuredSequenceDraft Body,
    BasicBlock ConditionBlock,
    bool ContinueWhenConditionTrue,
    StructuredSequenceDraft ContinueBody,
    StructuredSequenceDraft ExitBody) : StructuredRegionDraft
{
    public bool IsOriginal { get; init; } = true;
}
