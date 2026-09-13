using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentPackageExecution
{
    void Run(ComponentPackageRequest request, ComponentPackageWorkspace workspace);
}

public sealed class ComponentPackageExecution(
    IComponentPackageOperationRunner operations,
    IComponentCoreModuleLinker coreModules,
    IFileMover files) : IComponentPackageExecution
{
    private readonly IComponentPackageOperationRunner _operations = operations ??
        throw new ArgumentNullException(nameof(operations));
    private readonly IComponentCoreModuleLinker _coreModules = coreModules ??
        throw new ArgumentNullException(nameof(coreModules));
    private readonly IFileMover _files = files ??
        throw new ArgumentNullException(nameof(files));

    public void Run(ComponentPackageRequest request, ComponentPackageWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        var componentCoreModule = request.CoreModulePath;
        if (request.RuntimeModulePath is not null)
        {
            _coreModules.Link(new(
                request.CoreModulePath,
                request.RuntimeModulePath,
                workspace.LinkedModulePath,
                request.Target,
                request.ManagedExecutableEntryPoint));
            componentCoreModule = workspace.LinkedModulePath;
        }
        var embedArguments = new List<string>
        {
            "component", "embed", request.WitPath, componentCoreModule,
            "--encoding", request.Target.CanonicalStringEncoding,
            "--output", workspace.EmbeddedComponentPath,
        };
        if (request.World is not null)
        {
            embedArguments.Add("--world");
            embedArguments.Add(request.World);
        }
        _operations.Run(embedArguments, "embed component metadata");
        _operations.Run(
            ["component", "new", workspace.EmbeddedComponentPath,
                "--output", workspace.ComponentPath],
            "create component");
        _operations.Run(["validate", workspace.ComponentPath, "--features", "all"],
            "validate component");
        _files.Move(workspace.ComponentPath, request.OutputPath);
    }
}
