using System;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawModuleLinker
{
    void Link(RawModuleLinkRequest request);
}

public sealed class RawModuleLinker(
    IRawModuleLinkInputValidator inputs,
    IComponentPackageWorkspaceFactory workspaces,
    IRawModuleLinkExecution execution) : IRawModuleLinker
{
    private readonly IRawModuleLinkInputValidator _inputs = inputs ??
        throw new ArgumentNullException(nameof(inputs));
    private readonly IComponentPackageWorkspaceFactory _workspaces = workspaces ??
        throw new ArgumentNullException(nameof(workspaces));
    private readonly IRawModuleLinkExecution _execution = execution ??
        throw new ArgumentNullException(nameof(execution));

    public void Link(RawModuleLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _inputs.Validate(request);
        var workspace = _workspaces.Create(request.OutputPath);
        _execution.Run(request, workspace);
    }
}
