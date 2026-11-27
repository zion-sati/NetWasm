// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

// Runs each step and keeps its name, in the order the steps finish.
internal sealed class RecordingAnalysisSteps : IAnalysisStepRecorder
{
    public List<string> Steps { get; } = [];

    public T Record<T>(string step, Func<T> run)
    {
        var result = run();
        Steps.Add(step);
        return result;
    }
}
