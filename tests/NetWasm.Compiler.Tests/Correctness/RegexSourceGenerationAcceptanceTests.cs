using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class RegexSourceGenerationAcceptanceTests
{
    [Fact]
    public void OrdinarySdkGeneratorEmitsOnlyCustomRunnersForSupportedCorpus()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        using var built = SourceGenerationFixtureBuild.CreateRegex(
            environment,
            processes,
            CilProfile.Debug);

        Assert.Equal(
            8,
            CountOccurrences(built.GeneratedText, "private sealed class RunnerFactory"));
        Assert.Equal(
            8,
            CountOccurrences(built.GeneratedText, "override void Scan"));
        Assert.DoesNotContain(
            "A custom Regex-derived type could not be generated",
            built.GeneratedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RegexCompiler",
            built.GeneratedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RegexLWCGCompiler",
            built.GeneratedText,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void OrdinarySourceGeneratedRegexExecutesForEveryProfileAndTarget(
        string profileName,
        WasmTarget target) => AssertExecutes(
            profileName,
            target,
            SourceGenerationFixtureBuild.CreateRegex);

    [Fact]
    public void OrdinarySdkGeneratorEmitsIntentionalFallbacks()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        using var built = SourceGenerationFixtureBuild.CreateRegexFallback(
            environment,
            processes,
            CilProfile.Debug);

        Assert.Equal(
            2,
            CountOccurrences(
                built.GeneratedText,
                "A custom Regex-derived type could not be generated"));
        Assert.DoesNotContain(
            "private sealed class RunnerFactory",
            built.GeneratedText,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void IntentionalGeneratedFallbackExecutesForEveryProfileAndTarget(
        string profileName,
        WasmTarget target) => AssertExecutes(
            profileName,
            target,
            SourceGenerationFixtureBuild.CreateRegexFallback);

    private static void AssertExecutes(
        string profileName,
        WasmTarget target,
        Func<CompilerCorrectnessEnvironment,
            IQualifiedProcessRunner,
            CilProfile,
            SourceGenerationFixtureBuild> build)
    {
        var profile = Enum.Parse<CilProfile>(profileName);
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        var desktop = services.GetRequiredService<IDesktopOracleRunner>();
        var netWasm = services.GetRequiredService<INetWasmOracleRunner>();
        using var built = build(environment, processes, profile);

        var expected = desktop.Run(built.Compilation);
        var execution = netWasm.CompileAndRun(built.Compilation, target);

        Assert.True(execution.Executed);
        Assert.Equal(target, execution.Target);
        Assert.Equal(expected.Keys.Order(), execution.Observations.Keys.Order());
        var failures = new List<string>();
        foreach (var input in expected.Keys)
        {
            var observation = execution.Observations[input];
            if (observation.Kind != OracleObservationKind.Value ||
                observation.Value != expected[input].Value ||
                observation.ExceptionType is not null)
            {
                failures.Add(
                    $"input={input}, kind={observation.Kind}, " +
                    $"actual={observation.Value}, expected={expected[input].Value}, " +
                    $"detail={observation.Detail}, trace={observation.Trace}");
            }
        }

        Assert.Empty(failures);
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }

        return count;
    }
}
