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
