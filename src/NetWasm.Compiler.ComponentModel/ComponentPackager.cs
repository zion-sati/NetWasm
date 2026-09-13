using System;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentPackager
{
    void Package(ComponentPackageRequest request);
}

public sealed class ComponentPackager(
    IComponentPackageInputValidator inputs,
    IComponentPackageWorkspaceFactory workspaces,
    IComponentPackageExecution execution,
    IComponentPackagingCapability capability) : IComponentPackager
{
    private readonly IComponentPackageInputValidator _inputs = inputs ??
        throw new ArgumentNullException(nameof(inputs));
    private readonly IComponentPackageWorkspaceFactory _workspaces = workspaces ??
        throw new ArgumentNullException(nameof(workspaces));
    private readonly IComponentPackageExecution _execution = execution ??
        throw new ArgumentNullException(nameof(execution));
    private readonly IComponentPackagingCapability _capability = capability ??
        throw new ArgumentNullException(nameof(capability));

    public void Package(ComponentPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _capability.EnsureSupported(request.Target);
        _inputs.Validate(request);
        using var workspace = _workspaces.Create(request.OutputPath);
        _execution.Run(request, workspace);
    }
}
