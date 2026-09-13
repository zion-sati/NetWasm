using System;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ManagedBoundaryPlanBuilder : IManagedBoundaryPlanBuilder
{
    private readonly IManagedBoundaryFailureDispositionResolver _policy;

    public ManagedBoundaryPlanBuilder(
        IManagedBoundaryFailureDispositionResolver policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public ManagedBoundaryPlanEntry Build(ManagedBoundaryPlanBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var functionOffset = request.FunctionIndex - request.ImportedFunctionCount;
        if (functionOffset < 0 || functionOffset >= request.Functions.Count)
            throw new InvalidOperationException(
                "A managed boundary must identify a defined function.");

        var function = request.Functions[functionOffset];
        return new(
            request.FunctionIndex,
            function.Name,
            request.ExportName,
            request.Kind,
            request.IsOutwardFacing,
            _policy.Resolve(request.Kind));
    }

}
