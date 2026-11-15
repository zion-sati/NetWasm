// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness.TimeZoneInfoFacadeQualification;

internal static class TimeZoneInfoFacadeCorpus
{
    private const string FixtureRelativePath =
        "tests/end-to-end/timezoneinfo-facade/TimeZoneInfoFacadeFixture.cs";
    private const string FixtureName = "NetWasm.TimeZoneInfo.Facade";
    private const string FixtureNamespace = "NetWasm.Tests.TimeZoneInfoFacade.Fixture";

    internal static CorpusFixture Create(CompilerCorrectnessEnvironment environment) =>
        Create(environment, "Run", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);

    internal static CorpusFixture CreateMissingAsset(
        CompilerCorrectnessEnvironment environment) =>
        Create(environment, "RunMissingAsset", [0]);

    internal static CorpusFixture CreateLocalSemantics(
        CompilerCorrectnessEnvironment environment) =>
        Create(environment, "RunLocalSemantics", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

    internal static CorpusFixture CreateCorruptAsset(
        CompilerCorrectnessEnvironment environment) =>
        Create(environment, "RunCorruptAsset", [0]);

    private static CorpusFixture Create(
        CompilerCorrectnessEnvironment environment,
        string entryMethod,
        ImmutableArray<int> inputs)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var sourcePath = Path.Combine(environment.RepositoryRoot, FixtureRelativePath);
        return new CorpusFixture(
            FixtureName,
            FixtureNamespace,
            File.ReadAllText(sourcePath),
            inputs)
        {
            DesktopEntryMethod = entryMethod,
            WasmEntryMethod = entryMethod,
            ExecuteWasm64 = true,
            SupportsBatchedOracle = true,
            EmitStackTrace = false,
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "TimeZoneInfo is a framework facade over the selected NetWasm timezone asset.",
        };
    }
}
