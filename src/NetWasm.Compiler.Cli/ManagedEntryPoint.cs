// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Cli;

internal sealed record ManagedEntryPoint(
    string TypeName,
    string MethodName,
    int MetadataToken,
    ManagedExecutableEntryPointAbi Abi);
