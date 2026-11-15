// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Toolchain.Prerequisites;

public enum HostToolCompatibilityFailure
{
    ToolIdentityMismatch,
    VersionBelowMinimum,
    VersionAtOrAboveMaximum,
    MissingCapability,
}

public sealed class HostToolCompatibilityException : InvalidOperationException
{
    public HostToolCompatibilityException(
        string toolId,
        HostToolCompatibilityFailure failure,
        string message)
        : base(message)
    {
        ToolId = toolId;
        Failure = failure;
    }

    public string ToolId { get; }

    public HostToolCompatibilityFailure Failure { get; }
}
