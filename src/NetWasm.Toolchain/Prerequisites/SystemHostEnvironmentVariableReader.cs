// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class SystemHostEnvironmentVariableReader : IHostEnvironmentVariableReader
{
    public string? Read(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Environment.GetEnvironmentVariable(name);
    }
}
