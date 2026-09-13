using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ComponentBuildRequest(
    string CoreModulePath,
    string RuntimeModulePath,
    string WitPath,
    string? World,
    string OutputPath,
    ComponentTarget Target,
    ComponentManifestInputs ManifestInputs,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint = null);
