// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentPackageInputValidator
{
    void Validate(ComponentPackageRequest request);
}

public sealed class ComponentPackageInputValidator(
    IFileExistence files,
    IDirectoryExistence directories) : IComponentPackageInputValidator
{
    private readonly IFileExistence _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IDirectoryExistence _directories = directories ??
        throw new ArgumentNullException(nameof(directories));

    public void Validate(ComponentPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RuntimeModulePath is null &&
            request.OptimizationOverrides is { ManagedInliningHints: not ManagedInliningHints.Auto })
            throw ComponentException.Invalid(
                "Managed inlining hint overrides require core-module linking with a runtime module.");
        RequireFile(request.CoreModulePath, "core module");
        if (!_files.Exists(request.WitPath) && !_directories.Exists(request.WitPath))
        {
            throw ComponentException.Invalid(
                $"WIT path '{request.WitPath}' does not exist");
        }
        if (request.ManagedExecutableEntryPoint is not null &&
            request.RuntimeModulePath is null)
        {
            throw ComponentException.Invalid(
                "managed executable component packaging requires a runtime core module");
        }
    }

    private void RequireFile(string path, string description)
    {
        if (!_files.Exists(path))
        {
            throw ComponentException.Invalid($"{description} '{path}' does not exist");
        }
    }
}
