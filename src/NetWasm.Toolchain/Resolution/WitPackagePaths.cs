// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Toolchain.Resolution;

public sealed record WitPackagePaths(
    string PackageId,
    string PackageVersion,
    string CommandPath,
    string DiagnosticCommandPath,
    string CompilerPath);

public interface IWitPackagePathResolver
{
    WitPackagePaths Resolve();
}
