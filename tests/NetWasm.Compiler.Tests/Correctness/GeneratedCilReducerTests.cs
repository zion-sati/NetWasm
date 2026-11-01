using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedCilReducerTests
{
    private static readonly GeneratedCilFailureFingerprint Fingerprint = new(
        "semantic-mismatch",
        "wasm-execution",
        "return-value");

    [Fact]
    public void ReducesASeededValidProgramDeterministicallyAndPreservesFingerprint()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var reducer = services.GetRequiredService<IGeneratedCilReducer>();
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var request = new GeneratedCilReductionRequest(
            original,
            Fingerprint,
            (_, _) => Fingerprint,
            TimeSpan.FromSeconds(5));

        var first = reducer.Reduce(request);
        var second = reducer.Reduce(request);

        Assert.False(first.TimedOut);
        var serializer = new GeneratedCilSerializer(new GeneratedCilValidator());
        Assert.Equal<int>(first.Reduced.Inputs, second.Reduced.Inputs);
        Assert.Equal(
            serializer.Serialize(first.Reduced.Program),
            serializer.Serialize(second.Reduced.Program));
        Assert.Equal<string>(first.AppliedPasses, second.AppliedPasses);
        Assert.True(GeneratedCilReductionScore.Measure(first.Reduced).CompareTo(
            GeneratedCilReductionScore.Measure(original)) < 0);
        new GeneratedCilValidator().Validate(first.Reduced.Program);
        Assert.Equal(Fingerprint, request.Oracle(first.Reduced, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReturnsBestValidCandidateWhenDeadlineExpires()
    {
        var clock = new AdvancingTimeProvider();
        var reducer = new GeneratedCilReducer(
            [new InputReductionPass(), new InstructionReductionPass()],
            new GeneratedCilValidator(),
            clock);
        var original = GeneratedCilReductionTestPrograms.StraightLine();

        var result = reducer.Reduce(new(
            original,
            Fingerprint,
            (_, _) => Fingerprint,
            TimeSpan.FromTicks(3)));

        Assert.True(result.TimedOut);
        new GeneratedCilValidator().Validate(result.Reduced.Program);
    }

    [Fact]
    public void ReplaysAndReducesInjectedMismatchInARealPilotSeed()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var reducer = services.GetRequiredService<IGeneratedCilReducer>();
        var serializer = services.GetRequiredService<IGeneratedCilSerializer>();
        var generator = new RandomCilGenerator();
        var seed = RandomCilFixtureFactory.PilotSeeds.First(candidate =>
            generator.Generate(candidate).Operations.Contains(
                CilOperation.MultiplyCheckedUnsigned));
        var program = generator.Generate(seed);
        var original = new GeneratedCilReductionCase(
            program,
            RandomCilFixtureFactory.Create(seed).Inputs);
        GeneratedCilFailureFingerprint? Oracle(
            GeneratedCilReductionCase candidate,
            CancellationToken _) =>
            candidate.Inputs.Contains(19) &&
            candidate.Program.Operations.Contains(CilOperation.MultiplyCheckedUnsigned)
                ? Fingerprint
                : null;

        var first = reducer.Reduce(new(
            original,
            Fingerprint,
            Oracle,
            TimeSpan.FromSeconds(10)));
        var second = reducer.Reduce(new(
            original,
            Fingerprint,
            Oracle,
            TimeSpan.FromSeconds(10)));

        Assert.False(first.TimedOut);
        Assert.Equal(Fingerprint, Oracle(first.Reduced, TestContext.Current.CancellationToken));
        Assert.True(
            serializer.Serialize(first.Reduced.Program).Length <
            serializer.Serialize(original.Program).Length);
        Assert.Equal(
            serializer.Serialize(first.Reduced.Program),
            serializer.Serialize(second.Reduced.Program));
        Assert.Equal<int>(first.Reduced.Inputs, second.Reduced.Inputs);
        Assert.Contains(19, first.Reduced.Inputs);
        Assert.Contains(
            CilOperation.MultiplyCheckedUnsigned,
            first.Reduced.Program.Operations);
        new GeneratedCilValidator().Validate(first.Reduced.Program);
    }

    [Fact]
    public void CancelsACooperativeFailureOracleAtTheHardDeadline()
    {
        var reducer = new GeneratedCilReducer(
            [new InputReductionPass()],
            new GeneratedCilValidator(),
            TimeProvider.System);
        var original = GeneratedCilReductionTestPrograms.StraightLine();

        var result = reducer.Reduce(new(
            original,
            Fingerprint,
            (_, cancellationToken) =>
            {
                cancellationToken.WaitHandle.WaitOne();
                return null;
            },
            TimeSpan.FromMilliseconds(30)));

        Assert.True(result.TimedOut);
        Assert.Equal(original, result.Reduced);
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Increment(ref _timestamp);
    }
}
