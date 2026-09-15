namespace NetWasm.Compiler.ComponentModel.Browser;

/// <summary>Exact virtual paths for caller-owned link intermediates.</summary>
public sealed record BrowserComponentCoreModuleWorkspace(
    string EnvironmentModulePath,
    string HostModulePath,
    string ManagedExecutableAdapterModulePath,
    string MergedModulePath,
    string SanitizedModulePath);
