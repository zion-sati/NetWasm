using System;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentCoreModuleWorkspaceFactory
{
    ComponentCoreModuleWorkspace Create(string outputPath);
}

public sealed class ComponentCoreModuleWorkspaceFactory(IFileDeleter deletions) :
    IComponentCoreModuleWorkspaceFactory
{
    private readonly IFileDeleter _deletions = deletions ??
        throw new ArgumentNullException(nameof(deletions));

    public ComponentCoreModuleWorkspace Create(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return new ComponentCoreModuleWorkspace(
            _deletions,
            outputPath + ".environment.wasm",
            outputPath + ".netwasm-host.wasm",
            outputPath + ".managed-executable.wasm",
            outputPath + ".merged.wasm",
            outputPath + ".sanitized.wasm");
    }
}
