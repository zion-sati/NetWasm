// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Browser;

public sealed record BrowserCompilationEntryPoint(
    string AssemblyPath,
    string TypeName,
    string MethodName,
    int? Token,
    CompilerEntryPointKind Kind,
    ManagedExecutableEntryPointAbi? Abi = null);
