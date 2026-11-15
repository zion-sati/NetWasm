// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Packing;

internal sealed record PackTaskDependencies(
    IMsBuildPackInputAdapter InputAdapter,
    IProjectReferenceAdapter ProjectReferenceAdapter,
    IPackageBuilder PackageBuilder,
    IPackDiagnosticWriter DiagnosticWriter);
