// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed record ManagedExecutableComponentAdapterRequest(
    string OutputPath,
    ComponentTarget Target,
    ManagedExecutableEntryPointAbi EntryPoint,
    bool StructuredDiagnostics = false);
