// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Execution;

/// <summary>Local build output locating the host and immutable deployment to execute.</summary>
/// <remarks>Contains machine-local paths. Never publish this descriptor with an application.</remarks>
public sealed record ExecutionDescriptor(
    int SchemaVersion,
    string BuildFingerprint,
    string DeploymentManifestPath,
    string DeploymentManifestSha256,
    string HostingVersion,
    string HostExecutablePath,
    string LauncherPath);
