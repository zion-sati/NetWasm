using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structured;

public readonly record struct StructuredBlockId(int Value);

public readonly record struct StructuredExceptionGroupId(int Value);

public readonly record struct StructuredContinuationId(int Value);

public sealed record StructuredMethodHeader(
    MethodDefinitionModel Method,
    MethodInstanceModel? MethodInstance,
    int MaxStack,
    ImmutableArray<CliValueKind> Locals,
    ImmutableArray<CliTypeIdentity> LocalSignatureTypes,
    ImmutableArray<CilInstruction> Instructions);

public sealed record StructuredMethod(
    StructuredMethodHeader Header,
    StructuredBlockId EntryBlock,
    ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> Blocks,
    StructuredSequence Body,
    ImmutableArray<StructuredExceptionGroupId> TopLevelExceptionGroups,
    ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup> ExceptionGroups,
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> InstructionEntryStacks);

public sealed record StructuredBlockDefinition(
    StructuredBlockId Id,
    int StartOffset,
    ImmutableArray<CilInstruction> Instructions,
    ImmutableArray<CliValueKind> EntryStack,
    StructuredBlockExit Exit)
{
    public required int EndOffset { get; init; }
}

public abstract record StructuredBlockExit;

public sealed record StructuredFallthroughExit(StructuredBlockId? Target) : StructuredBlockExit;

public sealed record StructuredBranchExit(int InstructionOffset, StructuredBlockId Target) : StructuredBlockExit;

public sealed record StructuredConditionalExit(
    StructuredCondition Condition,
    StructuredBlockId WhenTaken,
    StructuredBlockId WhenNotTaken) : StructuredBlockExit;

public sealed record StructuredLeaveExit(
    int InstructionOffset,
    StructuredBlockId Target) : StructuredBlockExit;

public sealed record StructuredTerminalExit(CilInstruction Instruction) : StructuredBlockExit;

public sealed record StructuredCondition(
    int InstructionOffset,
    CilOperation Operation,
    int StackSlot,
    CliValueKind LeftKind,
    CliValueKind? RightKind);

public enum StructuredBlockRole
{
    Owner,
    ExecutingReplica,
    RoutingReplica,
}

public sealed record StructuredBlockOccurrence(
    StructuredBlockId Block,
    StructuredBlockRole Role,
    StructuredContinuationId? LeaveContinuation = null);

public abstract record StructuredRegion;

public sealed record StructuredSequence(ImmutableArray<StructuredRegion> Regions) : StructuredRegion
{
    public static StructuredSequence Empty { get; } = new([]);
}

public sealed record StructuredCode(StructuredBlockOccurrence Occurrence) : StructuredRegion;

public sealed record StructuredLoopBreak : StructuredRegion;

public sealed record StructuredLoopContinue : StructuredRegion;

public sealed record StructuredDispatcherContinue(StructuredBlockId Target) : StructuredRegion;

public sealed record StructuredExceptionRegion(
    StructuredExceptionGroupId Group,
    StructuredContinuationId? FallthroughContinuation) : StructuredRegion
{
    public ImmutableHashSet<StructuredContinuationId> DispatcherContinuations { get; init; } = [];
}

public sealed record StructuredDispatcher(
    StructuredBlockId? EntryBlock,
    ImmutableArray<StructuredDispatcherBlock> Blocks,
    ImmutableArray<StructuredDispatcherExit> Exits) : StructuredRegion;

public sealed record StructuredDispatcherBlock(
    StructuredBlockOccurrence Occurrence,
    StructuredBlockId? WhenTrue,
    StructuredBlockId? WhenFalse);

public sealed record StructuredDispatcherExit(
    StructuredBlockId Target,
    StructuredSequence Body);

public sealed record StructuredIf(
    StructuredBlockOccurrence Condition,
    StructuredSequence WhenTrue,
    StructuredSequence WhenFalse) : StructuredRegion;

public sealed record StructuredLoop(
    StructuredBlockOccurrence Condition,
    bool ContinueWhenConditionTrue,
    StructuredSequence Body,
    StructuredSequence ContinueBody,
    StructuredSequence ExitBody) : StructuredRegion;

public sealed record StructuredPostTestLoop(
    StructuredSequence Body,
    StructuredBlockOccurrence Condition,
    bool ContinueWhenConditionTrue,
    StructuredSequence ContinueBody,
    StructuredSequence ExitBody) : StructuredRegion;

public sealed record StructuredExceptionGroup(
    StructuredExceptionGroupId Id,
    StructuredExceptionGroupId? Parent,
    int TryOffset,
    int TryLength,
    ImmutableArray<StructuredExceptionPart> ProtectedParts,
    ImmutableArray<StructuredExceptionClause> Clauses,
    ImmutableArray<StructuredExceptionContinuation> NormalContinuations,
    StructuredDispatcher? ContinuationDispatcher,
    StructuredBlockId? ContinuationJoinBlock)
{
    public ImmutableArray<StructuredBlockId> ProtectedBlocks { get; init; } = [];
}

public sealed record StructuredExceptionContinuation(
    StructuredContinuationId Id,
    int TargetOffset,
    StructuredBlockId Target,
    StructuredSequence Body);

public abstract record StructuredExceptionPart;

public sealed record StructuredExceptionCode(StructuredSequence Body) : StructuredExceptionPart;

public sealed record StructuredNestedExceptionGroup(StructuredExceptionGroupId Group) : StructuredExceptionPart;

public sealed record StructuredExceptionClause(
    CilExceptionRegionKind Kind,
    int HandlerOffset,
    int HandlerLength,
    EntityKey? CatchType,
    int? FilterOffset,
    StructuredSequence HandlerBody,
    StructuredSequence? FilterBody)
{
    public required StructuredBlockId HandlerBlock { get; init; }

    public StructuredBlockId? FilterBlock { get; init; }
}
