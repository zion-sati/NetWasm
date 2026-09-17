namespace NetWasm.Compiler.ComponentModel;

using NetWasm.Compiler.Core.ManagedExecutables;

public sealed record ComponentCoreModuleLinkRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string OutputPath,
    ComponentTarget Target,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint = null,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Size);
