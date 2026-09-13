using System;

namespace NetWasm.Compiler.ComponentModel;

public sealed class ComponentCoreModuleWorkspace : IDisposable
{
    public ComponentCoreModuleWorkspace(
        IFileDeleter deletions,
        string environmentModulePath,
        string hostModulePath,
        string managedExecutableAdapterModulePath,
        string mergedModulePath,
        string sanitizedModulePath)
    {
        _deletions = deletions ?? throw new ArgumentNullException(nameof(deletions));
        EnvironmentModulePath = environmentModulePath ??
            throw new ArgumentNullException(nameof(environmentModulePath));
        HostModulePath = hostModulePath ??
            throw new ArgumentNullException(nameof(hostModulePath));
        ManagedExecutableAdapterModulePath = managedExecutableAdapterModulePath ??
            throw new ArgumentNullException(nameof(managedExecutableAdapterModulePath));
        MergedModulePath = mergedModulePath ??
            throw new ArgumentNullException(nameof(mergedModulePath));
        SanitizedModulePath = sanitizedModulePath ??
            throw new ArgumentNullException(nameof(sanitizedModulePath));
    }

    public string EnvironmentModulePath { get; }

    public string HostModulePath { get; }

    public string ManagedExecutableAdapterModulePath { get; }

    public string MergedModulePath { get; }

    public string SanitizedModulePath { get; }

    private readonly IFileDeleter _deletions;

    public void Dispose()
    {
        _deletions.Delete(EnvironmentModulePath);
        _deletions.Delete(HostModulePath);
        _deletions.Delete(ManagedExecutableAdapterModulePath);
        _deletions.Delete(MergedModulePath);
        _deletions.Delete(SanitizedModulePath);
    }
}
