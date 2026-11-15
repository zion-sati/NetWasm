// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed record RawInspectionToolchainPaths(
    string PackageId,
    string PackageVersion,
    string NodePath,
    Version NodeVersion,
    string InspectionCommandPath,
    string BinaryenModulePath,
    string WasmOptPath,
    string WasmMergePath,
    string BinaryenVersion);

public interface IRawInspectionToolchainPathResolver
{
    RawInspectionToolchainPaths Resolve(
        ResolvedHostExecutable nodeExecutable,
        ValidatedHostToolCompatibility nodeCompatibility);
}
