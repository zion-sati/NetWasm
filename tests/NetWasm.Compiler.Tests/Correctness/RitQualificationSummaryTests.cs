// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RitQualificationSummaryTests
{
    [Fact]
    public void AtomicSummaryRoundTripsOnlyCompleteExhaustivePassEvidence()
    {
        var directory = CorrectnessTestAssets.CreateDirectory();
        var path = Path.Combine(directory, "summary.json");
        var started = DateTimeOffset.UtcNow;
        var summary = new RitQualificationSummary(
            1,
            "PASS",
            true,
            RandomCilQualificationWorkload.ExhaustiveProgramCount,
            RandomCilQualificationWorkload.ExhaustiveShardCount,
            RandomCilQualificationWorkload.ExhaustiveShardCount,
            0,
            10,
            started,
            started.AddSeconds(1),
            1_000);

        RitQualificationSummaryStore.WriteAtomic(path, summary);

        Assert.Equal(summary, RitQualificationSummaryStore.ReadAndValidatePass(path));
        var text = File.ReadAllText(path);
        Assert.Contains('\n', text);
        Assert.DoesNotContain('\r', text);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData("FAIL", true, 2074, 0)]
    [InlineData("PASS", false, 2074, 0)]
    [InlineData("PASS", true, 2073, 0)]
    [InlineData("PASS", true, 2074, 1)]
    public void IncompleteOrFailedSummaryIsRejected(
        string status,
        bool exhaustive,
        int completed,
        int failed)
    {
        var directory = CorrectnessTestAssets.CreateDirectory();
        var path = Path.Combine(directory, "summary.json");
        var now = DateTimeOffset.UtcNow;
        RitQualificationSummaryStore.WriteAtomic(path, new(
            1,
            status,
            exhaustive,
            RandomCilQualificationWorkload.ExhaustiveProgramCount,
            RandomCilQualificationWorkload.ExhaustiveShardCount,
            completed,
            failed,
            1,
            now,
            now,
            0));

        Assert.Throws<InvalidDataException>(
            () => RitQualificationSummaryStore.ReadAndValidatePass(path));
        Directory.Delete(directory, recursive: true);
    }
}
