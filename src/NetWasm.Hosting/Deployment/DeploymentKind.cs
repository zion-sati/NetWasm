// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Deployment;

/// <summary>The form of the immutable artifact set described by a deployment manifest.</summary>
public enum DeploymentKind
{
    Component,
    Raw,
    Browser,
}
