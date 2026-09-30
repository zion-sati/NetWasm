namespace NetWasm.Compiler.Analysis;

internal interface IMemberDescriptorPlanner
{
    MemberDescriptorPlan Build(MemberDescriptorPlanningRequest request);
}
