// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class UnsupportedHostToolException : InvalidOperationException
{
    public UnsupportedHostToolException(string toolId)
        : base($"Host tool '{toolId}' has no compatibility validator.")
    {
        ToolId = toolId;
    }

    public string ToolId { get; }
}
