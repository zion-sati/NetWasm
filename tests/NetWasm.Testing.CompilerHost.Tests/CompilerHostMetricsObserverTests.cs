using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Testing.CompilerHost.Tests;

public sealed class CompilerHostMetricsObserverTests
{
    [Fact]
    public void CapturesTheReportedCompilerMetrics()
    {
        var observer = new CompilerHostMetricsObserver();
        var report = new CompilerMetricsReport(
            CompilerMetricsOutcome.Succeeded,
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            []);

        ((ICompilerMetricsObserver)observer).Report(report);

        Assert.Same(report, observer.Report);
        Assert.Throws<ArgumentNullException>(() =>
            ((ICompilerMetricsObserver)observer).Report(null!));
    }
}
