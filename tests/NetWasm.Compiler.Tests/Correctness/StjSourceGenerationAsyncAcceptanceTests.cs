using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class StjSourceGenerationAsyncAcceptanceTests
{
    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void GeneratedAsyncStreamingExecutesForEveryProfileAndTarget(
        string profileName,
        WasmTarget target)
    {
        ExecuteAndAssert(
            profileName,
            target,
            SourceGenerationFixtureBuild.CreateAsync);
    }

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void GeneratedNullablePipeRoundTripExecutesForEveryProfileAndTarget(
        string profileName,
        WasmTarget target)
    {
        ExecuteAndAssert(
            profileName,
            target,
            SourceGenerationFixtureBuild.CreateAsyncPipeRoundTrip);
    }

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void GeneratedStreamRoundTripExecutesForEveryProfileAndTarget(
        string profileName,
        WasmTarget target) => ExecuteAndAssert(
        profileName,
        target,
        SourceGenerationFixtureBuild.CreateAsyncStreamRoundTrip);

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void GeneratedSequenceRoundTripExecutesForEveryProfileAndTarget(
        string profileName,
        WasmTarget target) => ExecuteAndAssert(
        profileName,
        target,
        SourceGenerationFixtureBuild.CreateAsyncSequenceRoundTrip);

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void GeneratedSequenceFailuresExecuteForEveryProfileAndTarget(
        string profileName,
        WasmTarget target) => ExecuteAndAssert(
        profileName,
        target,
        SourceGenerationFixtureBuild.CreateAsyncSequenceFailures);

    private static void ExecuteAndAssert(
        string profileName,
        WasmTarget target,
        Func<
            CompilerCorrectnessEnvironment,
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
        foreach (var input in expected.Keys)
        {
            var observation = execution.Observations[input];
            Assert.True(
                observation.Kind == OracleObservationKind.Value,
                $"{profile}/{target}/input={input}: " +
                $"kind={observation.Kind}, detail={observation.Detail}, " +
                $"exception={observation.ExceptionType}, trace={observation.Trace}");
            Assert.Equal(expected[input].Value, observation.Value);
            Assert.Null(observation.ExceptionType);
        }
    }
}
