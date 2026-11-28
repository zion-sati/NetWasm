// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

// One case per generated program, so the shard planner can spread them and a hang names its case.
// NETWASM_GENERATED_CASE, when set, runs only the case it names.
public sealed class DeterministicGeneratedCorpusTests(CorrectnessTestRunner runner)
{
    public static TheoryData<string> PullRequestCases()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        return [.. services.GetRequiredService<ILanguageCorpusGenerator>().GeneratePullRequestCorpus()
            .Select(generated => generated.Name)];
    }

    [Theory]
    [MemberData(nameof(PullRequestCases))]
    public void PullRequestCorpusMatchesDesktopDotNet(string name)
    {
        var requested = Environment.GetEnvironmentVariable("NETWASM_GENERATED_CASE");
        if (!string.IsNullOrEmpty(requested) && requested != name)
        {
            return;
        }
        runner.RunGenerated(Assert.Single(runner.GeneratedCases, generated => generated.Name == name));
    }
}
