// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Provenance;

public sealed record SdkProvenanceFileInput(
    string SourcePath,
    string PackagePath,
    string? LogicalSourcePath = null);

public sealed record SdkProvenanceManifestInput(
    string PackageId,
    string PackageVersion,
    string SourceRevision,
    string ManifestPackagePath,
    IReadOnlyList<SdkProvenanceFileInput> PackageFiles,
    IReadOnlyList<SdkProvenanceFileInput> SourceFiles);
