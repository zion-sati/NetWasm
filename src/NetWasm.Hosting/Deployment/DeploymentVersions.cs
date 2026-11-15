// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Deployment;

/// <summary>Product and tool identities that produced a deployment.</summary>
public sealed record DeploymentVersions(
    string Sdk,
    string Compiler,
    string Runtime,
    string RuntimeAbi,
    string Hosting,
    string Toolchain);
