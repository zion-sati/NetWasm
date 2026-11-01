using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness.TimeZoneInfoFacadeQualification;

public sealed class TimeZoneInfoFacadeAcceptanceTests
{
    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void LocalAmbiguityKindAndBoundsMatchPinnedContracts(
        string profileName,
        WasmTarget target)
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var compiler = services.GetRequiredService<IRoslynCorpusCompiler>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        var netWasm = new TimeZoneInfoFacadeExecutionHarness(environment, processes);
        var profile = Enum.Parse<CilProfile>(profileName);
        var directory = CorrectnessTestAssets.CreateDirectory();
        try
        {
            var fixture = TimeZoneInfoFacadeCorpus.CreateLocalSemantics(environment);
            var compilation = compiler.Compile(fixture, profile, directory);
            using var asset = TimeZoneInfoFacadeAssetConfiguration.FromEnvironment();
            var execution = netWasm.Run(compilation, target, asset);

            foreach (var observation in execution.Observations)
            {
                Assert.True(
                    observation.Value.Kind == OracleObservationKind.Value,
                    $"input={observation.Key}, kind={observation.Value.Kind}, " +
                    $"exception={observation.Value.ExceptionType}");
            }
            Assert.Equal(1301, execution.Observations[0].Value);
            Assert.Equal(1401, execution.Observations[1].Value);
            Assert.Equal(1501, execution.Observations[2].Value);
            Assert.Equal(1601, execution.Observations[3].Value);
            Assert.Equal(1701, execution.Observations[4].Value);
            Assert.Equal(1801, execution.Observations[5].Value);
            Assert.Equal(1901, execution.Observations[6].Value);
            Assert.Equal(2001, execution.Observations[7].Value);
            Assert.Equal(2101, execution.Observations[8].Value);
            Assert.Equal(2201, execution.Observations[9].Value);
            Assert.Equal(2301, execution.Observations[10].Value);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void PublicFacadeMatchesDesktopOracleAcrossProfilesAndTargets(
        string profileName,
        WasmTarget target)
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var compiler = services.GetRequiredService<IRoslynCorpusCompiler>();
        var desktop = services.GetRequiredService<IDesktopOracleRunner>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        var netWasm = new TimeZoneInfoFacadeExecutionHarness(environment, processes);
        var profile = Enum.Parse<CilProfile>(profileName);
        var directory = CorrectnessTestAssets.CreateDirectory();
        try
        {
            var fixture = TimeZoneInfoFacadeCorpus.Create(environment);
            var compilation = compiler.Compile(fixture, profile, directory);
            var expected = desktop.Run(compilation, TestContext.Current.CancellationToken);
            using var asset = TimeZoneInfoFacadeAssetConfiguration.FromEnvironment();
            var execution = netWasm.Run(compilation, target, asset);

            Assert.Equal(target, execution.Target);
            Assert.Equal(expected.Keys.Order(), execution.Observations.Keys.Order());
            foreach (var input in expected.Keys)
            {
                var actual = execution.Observations[input];
                Assert.True(actual.Kind == OracleObservationKind.Value,
                    $"input {input}: {actual.Kind} {actual.ExceptionType} {actual.Detail}");
                Assert.Equal(expected[input].Value, actual.Value);
                Assert.Null(actual.ExceptionType);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // The host must execute these two fixtures with the deployment asset
    // respectively absent and corrupted. They intentionally bypass the desktop
    // oracle because desktop .NET uses the host's own timezone database. The
    // expected public contract is a loud PlatformNotSupportedException.
    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void MissingAssetFailsLoudlyAcrossProfilesAndTargets(
        string profileName,
        WasmTarget target)
    {
        AssertAssetFailure(
            profileName,
            target,
            TimeZoneInfoFacadeCorpus.CreateMissingAsset,
            "PlatformNotSupportedException",
            corruptAsset: false);
    }

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void CorruptAssetFailsLoudlyAcrossProfilesAndTargets(
        string profileName,
        WasmTarget target)
    {
        AssertAssetFailure(
            profileName,
            target,
            TimeZoneInfoFacadeCorpus.CreateCorruptAsset,
            "PlatformNotSupportedException",
            corruptAsset: true);
    }

    private static void AssertAssetFailure(
        string profileName,
        WasmTarget target,
        Func<CompilerCorrectnessEnvironment, CorpusFixture> createFixture,
        string expectedException,
        bool corruptAsset)
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var compiler = services.GetRequiredService<IRoslynCorpusCompiler>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        var netWasm = new TimeZoneInfoFacadeExecutionHarness(environment, processes);
        var profile = Enum.Parse<CilProfile>(profileName);
        var directory = CorrectnessTestAssets.CreateDirectory();
        try
        {
            using var asset = corruptAsset
                ? TimeZoneInfoFacadeAssetConfiguration.Corrupt(
                    ResolveAssetPath(),
                    directory,
                    "Australia/Melbourne")
                : TimeZoneInfoFacadeAssetConfiguration.Missing("Australia/Melbourne");
            var compilation = compiler.Compile(
                createFixture(environment),
                profile,
                directory);
            var execution = netWasm.Run(compilation, target, asset);
            var observation = Assert.Single(execution.Observations.Values);
            Assert.Equal(OracleObservationKind.ManagedException, observation.Kind);
            Assert.Equal("System." + expectedException, observation.ExceptionType);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        string ResolveAssetPath() =>
            Environment.GetEnvironmentVariable("NETWASM_TIMEZONE_ASSET_PATH")
            ?? throw new InvalidOperationException(
                "NETWASM_TIMEZONE_ASSET_PATH must name the selected timezone asset.");
    }
}
