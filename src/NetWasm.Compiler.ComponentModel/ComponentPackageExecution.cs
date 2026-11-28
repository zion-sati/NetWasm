// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Debugging;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentPackageExecution
{
    void Run(ComponentPackageRequest request, ComponentPackageWorkspace workspace);
}

public sealed class ComponentPackageExecution(
    IComponentPackageOperationRunner operations,
    IComponentCoreModuleLinker coreModules,
    IFileMover files,
    IPlainCoreModuleImportValidator plainCoreModuleImports,
    IWasmDebugInformationWriter debugSections) : IComponentPackageExecution
{
    private readonly IComponentPackageOperationRunner _operations = operations ??
        throw new ArgumentNullException(nameof(operations));
    private readonly IComponentCoreModuleLinker _coreModules = coreModules ??
        throw new ArgumentNullException(nameof(coreModules));
    private readonly IFileMover _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IPlainCoreModuleImportValidator _plainCoreModuleImports = plainCoreModuleImports ??
        throw new ArgumentNullException(nameof(plainCoreModuleImports));
    private readonly IWasmDebugInformationWriter _debugSections = debugSections ?? throw new ArgumentNullException(nameof(debugSections));

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
                request.ManagedExecutableEntryPoint,
                request.Optimization,
                request.StructuredDiagnostics)
            { OptimizationOverrides = request.OptimizationOverrides, DebugInformation = request.DebugInformation });
            componentCoreModule = workspace.LinkedModulePath;
            if (request.DebugInformation is { } debugInformation)
                _debugSections.Write(componentCoreModule, debugInformation);
        }
        if (request.PlainCoreModule)
        {
            _operations.Run(
                [.. Strip(request), componentCoreModule, "--output", workspace.ComponentPath],
                "strip core module metadata");
            _operations.Run(["validate", workspace.ComponentPath, "--features", "all"],
                "validate core module");
            _plainCoreModuleImports.Validate(workspace.ComponentPath);
            _files.Move(workspace.ComponentPath, request.OutputPath);
            return;
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
                "--output", workspace.UnstrippedComponentPath],
            "create component");
        _operations.Run(
            [.. Strip(request), workspace.UnstrippedComponentPath, "--output", workspace.ComponentPath],
            "strip component metadata");
        _operations.Run(["validate", workspace.ComponentPath, "--features", "all"],
            "validate component");
        _files.Move(workspace.ComponentPath, request.OutputPath);
    }

    // A product module keeps no custom section. With debug information the name section and the DWARF sections stay,
    // which a debugger and Wasmtime's trap backtraces read, and the rest goes: wasm-tools strip deletes the sections
    // whose names match --delete (and only those), and its regular expressions have no negative lookahead, so the
    // pattern spells out every name that is neither "name" nor starts with ".debug_".
    public const string NonDebugSections =
        @"^(?s:|[^.n].*|n|n[^a].*|na|na[^m].*|nam|nam[^e].*|name.+|\.|\.[^d].*|\.d|\.d[^e].*|\.de|\.de[^b].*|" +
        @"\.deb|\.deb[^u].*|\.debu|\.debu[^g].*|\.debug|\.debug[^_].*)$";

    private static string[] Strip(ComponentPackageRequest request) =>
        request.DebugInformation is null ? ["strip", "--all"] : ["strip", "--delete", NonDebugSections];
}
