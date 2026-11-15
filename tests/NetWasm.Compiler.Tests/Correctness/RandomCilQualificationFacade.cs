// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public static class RandomCilQualificationFacade
{
    public static async Task RunAsync(int? maximumShards, int startShard = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startShard);
        if (maximumShards is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumShards.Value, 1);
        }

        await using var services = CorrectnessTestAssets.CreateServices(Directory.GetCurrentDirectory());
        var planner = services.GetRequiredService<IRandomCilInteractionShardPlanner>();
        var availableShards = planner.CreateShards().Length;
        var selectedShards = maximumShards is null
            ? availableShards
            : Math.Min(maximumShards.Value, availableShards);
        var campaign = services.GetRequiredService<IRandomCilInteractionCampaign>();
        var results = await campaign.RunAsync(maximumShards, startShard);
        ValidateResults(results, selectedShards);
    }

    internal static void ValidateResults(
        System.Collections.Immutable.ImmutableArray<RandomCilCampaignResult> results,
        int maximumShards)
    {
        var expectedResults = maximumShards;
        var failures = results
            .Where(static result => result.Failure is not null)
            .Select(static result => result.Failure!)
            .ToList();
        if (results.Length != expectedResults)
        {
            failures.Add(new InvalidOperationException(
                $"RIT campaign returned {results.Length} of {expectedResults} expected paired shard results."));
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"RIT campaign did not complete successfully: completed={results.Length}/{expectedResults}, failures={failures.Count}.",
                failures);
        }
    }
}
