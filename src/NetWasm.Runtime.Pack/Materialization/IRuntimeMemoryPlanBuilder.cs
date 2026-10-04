namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeMemoryPlanBuilder
{
    RuntimeMemoryPlan Build(RuntimeMemoryLayoutRequest request);
}
