// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Diagnostics;

internal sealed record CompilerDiagnosticEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionStackTrace);
