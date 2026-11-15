// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed record RitQualificationSummary(
    int SchemaVersion,
    string Status,
    bool Exhaustive,
    int TotalPrograms,
    int TotalPairedShards,
    int CompletedPairedShards,
    int FailedPairedShards,
    int Workers,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    long ElapsedMilliseconds);

public static class RitQualificationSummaryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    public static void WriteAtomic(string path, RitQualificationSummary summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(summary);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + $".{Environment.ProcessId}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(summary, Options) + "\n");
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    public static RitQualificationSummary ReadAndValidatePass(string path)
    {
        var summary = JsonSerializer.Deserialize<RitQualificationSummary>(
            File.ReadAllText(path), Options) ??
            throw new InvalidDataException("RIT summary is empty.");
        if (summary.SchemaVersion != 1 || summary.Status != "PASS" ||
            !summary.Exhaustive ||
            summary.TotalPrograms != RandomCilQualificationWorkload.ExhaustiveProgramCount ||
            summary.TotalPairedShards != RandomCilQualificationWorkload.ExhaustiveShardCount ||
            summary.CompletedPairedShards != summary.TotalPairedShards ||
            summary.FailedPairedShards != 0 || summary.Workers <= 0 ||
            summary.FinishedAt < summary.StartedAt || summary.ElapsedMilliseconds < 0)
        {
            throw new InvalidDataException("RIT summary does not prove an exhaustive passing run.");
        }
        return summary;
    }
}
