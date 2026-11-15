// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Browser;

internal sealed class BrowserCompilerMetricsObserver(ICompilerMetricsObserver? inner) :
    ICompilerMetricsObserver
{
    public CompilerMetricsReport? Report { get; private set; }

    void ICompilerMetricsObserver.Report(CompilerMetricsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
        inner?.Report(report);
    }
}
