namespace NetWasm.Compiler.ComponentModel;

using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.Optimization;

public sealed record ComponentPackageRequest(
    string CoreModulePath,
    string WitPath,
    string? World,
    string OutputPath,
    ComponentTarget Target,
    string? RuntimeModulePath = null,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint = null,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Oz,
    bool StructuredDiagnostics = false)
{
    public WasmOptimizationOverrides? OptimizationOverrides { get; init; }

    // Write the linked core module itself (the program's world imports only, no WASI) rather
    // than a component.
    public bool PlainCoreModule { get; init; }
}
