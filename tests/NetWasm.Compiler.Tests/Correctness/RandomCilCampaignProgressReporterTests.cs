// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilCampaignProgressReporterTests
{
    [Fact]
    public void WritesLatestExactCampaignProgressToTheConfiguredOutput()
    {
        using var output = new StringWriter();
        var reporter = new ConsoleRandomCilCampaignProgressReporter(output);

        reporter.CampaignStarted(200, 1);
        reporter.Heartbeat(
            ImmutableArray<string>.Empty,
            37,
            200,
            TimeSpan.FromMinutes(2));

        Assert.Contains("completed=37/200", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsOnlyTheFirstFailureLineForLfAndCrLfMessages()
    {
        using var output = new StringWriter();
        var reporter = new ConsoleRandomCilCampaignProgressReporter(output);

        reporter.DiagnosticReplayFinished(
            "lf",
            TimeSpan.Zero,
            new InvalidOperationException("first-lf\nignored"),
            TimeSpan.Zero);
        reporter.DiagnosticReplayFinished(
            "crlf",
            TimeSpan.Zero,
            new InvalidOperationException("first-crlf\r\nignored"),
            TimeSpan.Zero);

        var text = output.ToString();
        Assert.Contains("detail=first-lf ", text, StringComparison.Ordinal);
        Assert.Contains("detail=first-crlf ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", text, StringComparison.Ordinal);
    }
}
