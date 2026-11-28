// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Debugging;
using System;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawModuleLinker
{
    void Link(RawModuleLinkRequest request);
}

public sealed class RawModuleLinker(
    IRawModuleLinkInputValidator inputs,
    IComponentPackageWorkspaceFactory workspaces,
    IRawModuleLinkExecution execution,
    IWasmDebugInformationWriter debugSections) : IRawModuleLinker
{
    private readonly IRawModuleLinkInputValidator _inputs = inputs ??
        throw new ArgumentNullException(nameof(inputs));
    private readonly IComponentPackageWorkspaceFactory _workspaces = workspaces ??
        throw new ArgumentNullException(nameof(workspaces));
    private readonly IRawModuleLinkExecution _execution = execution ??
        throw new ArgumentNullException(nameof(execution));
    private readonly IWasmDebugInformationWriter _debugSections = debugSections ?? throw new ArgumentNullException(nameof(debugSections));

    public void Link(RawModuleLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _inputs.Validate(request);
        var workspace = _workspaces.Create(request.OutputPath);
        _execution.Run(request, workspace);
        if (request.DebugInformation is { } debugInformation)
            _debugSections.Write(request.OutputPath, debugInformation);
    }
}
