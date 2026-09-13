using System;
using System.IO;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentPackageWorkspaceFactory
{
    ComponentPackageWorkspace Create(string outputPath);
}

public sealed class ComponentPackageWorkspaceFactory(IDirectoryCreator directories,
    IDirectoryDeleter deletions) : IComponentPackageWorkspaceFactory
{
    private readonly IDirectoryCreator _directories = directories ??
        throw new ArgumentNullException(nameof(directories));
    private readonly IDirectoryDeleter _deletions = deletions ??
        throw new ArgumentNullException(nameof(deletions));

    public ComponentPackageWorkspace Create(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        _directories.Create(outputDirectory);
        var temporaryDirectory = Path.Combine(
            outputDirectory,
            $".netwasm-component-{Guid.NewGuid():N}");
        _directories.Create(temporaryDirectory);
        return new ComponentPackageWorkspace(
            _deletions,
            temporaryDirectory,
            Path.Combine(temporaryDirectory, "linked.wasm"),
            Path.Combine(temporaryDirectory, "embedded.wasm"),
            Path.Combine(temporaryDirectory, "component.wasm"));
    }
}
