// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

internal static class UnrecordedAnalysisSteps
{
    // The compiler's step recorder with a log that takes no entries: each step only runs.
    public static IAnalysisStepRecorder Instance { get; } = new LoggingAnalysisStepRecorder(
        NullLogger<LoggingAnalysisStepRecorder>.Instance,
        new ProcessCompilerMemoryReader(hasProcess: false));
}
