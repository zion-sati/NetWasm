// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Testing.CompilerHost;

internal sealed class CompilerHostMetricsObserver : ICompilerMetricsObserver
{
    public CompilerMetricsReport? Report { get; private set; }

    void ICompilerMetricsObserver.Report(CompilerMetricsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
    }
}
