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
