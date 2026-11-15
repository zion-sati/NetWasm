// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Threading.Tasks;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemStandardErrorReader : IStandardErrorReader
{
    public Task<string> ReadAsync(IProcessSession process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return process.StandardError.ReadAsync();
    }
}
