// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Diagnostics;

// A compilation's diagnostic log: where it goes, and the least severe entries it keeps.
internal sealed record CompilerDiagnosticScope(string? Path, LogLevel MinimumLevel = LogLevel.Trace)
{
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Path);
}
