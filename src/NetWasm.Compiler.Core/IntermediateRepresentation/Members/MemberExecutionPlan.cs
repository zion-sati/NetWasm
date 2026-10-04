using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Members;

public sealed record MemberMethodExecutionPlan(
    MethodInstanceModel Descriptor,
    bool RequiresDispatch,
    ImmutableArray<DispatchTargetModel> Targets);

public sealed record DelegateDynamicInvokePlan(
    ImmutableDictionary<string, MethodInstanceModel> Methods,
    ImmutableHashSet<EntityKey> Invokers,
    MethodInstanceModel UnsupportedTarget,
    MethodInstanceModel ArgumentTarget,
    MethodInstanceModel ParameterCountTarget,
    MethodInstanceModel InvocationTarget);

public sealed record MemberExecutionPlan(
    ImmutableDictionary<string, MemberMethodExecutionPlan> Methods,
    ImmutableDictionary<string, FieldInstanceModel> Fields,
    MethodInstanceModel? UnsupportedTarget)
{
    public ImmutableHashSet<EntityKey> MethodInvokers { get; init; } = [];

    public DelegateDynamicInvokePlan? DelegateInvocation { get; init; }

    public static MemberExecutionPlan Empty { get; } = new(
        ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty,
        ImmutableDictionary<string, FieldInstanceModel>.Empty,
        null);
}
