// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Deployment;

/// <summary>Inputs required to prove that a local execution descriptor still names the exact deployment.</summary>
public sealed record DeploymentFreshnessRequest(
    ExecutionDescriptor ExecutionDescriptor,
    ReadOnlyMemory<byte> ManifestUtf8,
    ImmutableArray<DeploymentArtifactSnapshot> Artifacts);
