using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticsCoverageTests
{
    [Fact]
    public void DiagnosticFormatterProjectsEveryFieldAndRejectsMissingEntries()
    {
        var timestamp = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var formatter = new CompilerDiagnosticJsonLineFormatter();
        var line = formatter.Format(7, timestamp, new(
            "compiler", LogLevel.Error, new EventId(42, "failure"), "message",
            "InvalidOperationException", "broken", "stack"));

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal(7, root.GetProperty("Sequence").GetInt64());
        Assert.Equal("compiler", root.GetProperty("Category").GetString());
        Assert.Equal(42, root.GetProperty("EventId").GetInt32());
        Assert.Equal("stack", root.GetProperty("ExceptionStackTrace").GetString());
        Assert.Throws<ArgumentNullException>(() =>
            formatter.Format(0, timestamp, null!));
    }

    [Fact]
    public void AdapterTimingExposesItsPublicContract()
    {
        var timing = new CompilerAdapterTiming(
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(3), timing.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), timing.CompilerDuration);
        Assert.Equal(TimeSpan.FromSeconds(1), timing.ResidualDuration);
    }
}
