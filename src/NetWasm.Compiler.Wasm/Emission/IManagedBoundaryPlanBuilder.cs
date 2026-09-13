namespace NetWasm.Compiler.Wasm.Emission;

internal interface IManagedBoundaryPlanBuilder
{
    ManagedBoundaryPlanEntry Build(ManagedBoundaryPlanBuildRequest request);
}
