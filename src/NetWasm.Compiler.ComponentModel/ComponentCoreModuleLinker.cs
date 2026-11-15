// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentCoreModuleLinker
{
    void Link(ComponentCoreModuleLinkRequest request);
}

public sealed class ComponentCoreModuleLinker(
    IComponentCoreModuleInputValidator inputs,
    IComponentCoreModuleWorkspaceFactory workspaces,
    IComponentCoreModuleLinkExecution execution) : IComponentCoreModuleLinker
{
    private readonly IComponentCoreModuleInputValidator _inputs = inputs ??
        throw new ArgumentNullException(nameof(inputs));
    private readonly IComponentCoreModuleWorkspaceFactory _workspaces = workspaces ??
        throw new ArgumentNullException(nameof(workspaces));
    private readonly IComponentCoreModuleLinkExecution _execution = execution ??
        throw new ArgumentNullException(nameof(execution));

    public void Link(ComponentCoreModuleLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _inputs.Validate(request);
        var workspace = _workspaces.Create(request.OutputPath);
        _execution.Run(request, workspace);
    }
}
