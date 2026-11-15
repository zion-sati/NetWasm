// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Browser;

/// <summary>Exact virtual paths for caller-owned link intermediates.</summary>
public sealed record BrowserComponentCoreModuleWorkspace(
    string EnvironmentModulePath,
    string HostModulePath,
    string ManagedExecutableAdapterModulePath,
    string MergedModulePath,
    string SanitizedModulePath,
    string SanitizedRuntimeModulePath);
