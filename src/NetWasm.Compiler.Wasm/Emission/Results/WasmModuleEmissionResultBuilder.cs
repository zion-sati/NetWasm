using System;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Results;

internal sealed class WasmModuleEmissionResultBuilder(
    IWasmModuleBuilder modules,
    IManagedMethodEmissionMetricProjector metrics) :
    IWasmModuleEmissionResultBuilder
{
    public WasmModuleEmissionResult Build(WasmModuleEmissionBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var module = request.Module;
        return new(
            modules.Build(
                module.FunctionImports,
                module.MemoryImportModule,
                module.MemoryImportName,
                module.Functions,
                module.Exports,
                module.DataSegments,
                module.IncludeManagedExceptionTag,
                module.Target,
                module.IncludeNameSection),
            request.StaticDataEnd,
            metrics.Project(request.ManagedMethodEmissions),
            request.StackTraceSymbols)
        {
            FunctionImports = [.. module.FunctionImports],
            RuntimeFeatures = request.RuntimeFeatures,
        };
    }
}
