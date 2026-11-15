// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemProcessExitCodeReader : IProcessExitCodeReader
{
    public int Read(IProcessSession process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return process.ExitCode.Read();
    }
}
