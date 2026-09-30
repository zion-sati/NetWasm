using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Members;

public sealed record MemberMethodExecutionPlan(
    MethodInstanceModel Descriptor,
    bool RequiresDispatch,
    ImmutableArray<DispatchTargetModel> Targets);

public sealed record MemberExecutionPlan(
    ImmutableDictionary<string, MemberMethodExecutionPlan> Methods,
    ImmutableDictionary<string, FieldInstanceModel> Fields,
    MethodInstanceModel? UnsupportedTarget)
{
    public static MemberExecutionPlan Empty { get; } = new(
        ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty,
        ImmutableDictionary<string, FieldInstanceModel>.Empty,
        null);
}
