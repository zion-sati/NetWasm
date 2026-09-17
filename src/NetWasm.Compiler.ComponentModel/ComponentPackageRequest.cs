namespace NetWasm.Compiler.ComponentModel;

using NetWasm.Compiler.Core.ManagedExecutables;

public sealed record ComponentPackageRequest(
    string CoreModulePath,
    string WitPath,
    string? World,
    string OutputPath,
    ComponentTarget Target,
    string? RuntimeModulePath = null,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint = null,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Size);
