// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Deployment;

/// <summary>An immutable file in the deployable artifact closure.</summary>
public sealed record DeploymentArtifact(
    string RelativePath,
    string Role,
    string MediaType,
    string Sha256,
    int? SchemaVersion);
