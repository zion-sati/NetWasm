// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Deployment;

/// <summary>The observed digest of one artifact supplied to an in-memory freshness check.</summary>
public sealed record DeploymentArtifactSnapshot(string RelativePath, string Sha256);
