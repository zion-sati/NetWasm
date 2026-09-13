using System;

namespace NetWasm.Compiler.ComponentModel;

public sealed class ComponentPackageWorkspace : IDisposable
{
    public ComponentPackageWorkspace(
        IDirectoryDeleter deletions,
        string temporaryDirectory,
        string linkedModulePath,
        string embeddedComponentPath,
        string componentPath)
    {
        _deletions = deletions ?? throw new ArgumentNullException(nameof(deletions));
        TemporaryDirectory = temporaryDirectory ??
            throw new ArgumentNullException(nameof(temporaryDirectory));
        LinkedModulePath = linkedModulePath ??
            throw new ArgumentNullException(nameof(linkedModulePath));
        EmbeddedComponentPath = embeddedComponentPath ??
            throw new ArgumentNullException(nameof(embeddedComponentPath));
        ComponentPath = componentPath ??
            throw new ArgumentNullException(nameof(componentPath));
    }

    public string TemporaryDirectory { get; }

    public string LinkedModulePath { get; }

    public string EmbeddedComponentPath { get; }

    public string ComponentPath { get; }

    private readonly IDirectoryDeleter _deletions;

    public void Dispose() => _deletions.Delete(TemporaryDirectory);
}
