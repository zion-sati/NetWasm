using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusExecutionVerifierTests
{
    private readonly ICorpusExecutionVerifier _verifier = new CorpusExecutionVerifier();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AcceptsCompleteRequestedExecution(bool memory64, bool optimized)
    {
        var target = memory64 ? WasmTarget.Wasm64 : WasmTarget.Wasm32;
        _verifier.Verify(Fixture() with { ExecuteOptimizedWasm = optimized }, target, Execution(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsSkippedRequiredTargets(bool memory64)
    {
        var target = memory64 ? WasmTarget.Wasm64 : WasmTarget.Wasm32;
        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(Fixture(), target, Execution(target) with { Executed = false }));
    }

    [Fact]
    public void AcceptsExplicitValidationOnlyWasm64WithoutPretendingItExecuted() =>
        _verifier.Verify(Fixture() with { ExecuteWasm64 = false }, WasmTarget.Wasm64,
            Execution(WasmTarget.Wasm64) with
            {
                Executed = false,
                Observations = ImmutableDictionary<int, OracleObservation>.Empty,
                OptimizedObservations = ImmutableDictionary<int, OracleObservation>.Empty,
            });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsObservationsFromAnUnexecutedTarget(bool optimized)
    {
        var execution = Execution(WasmTarget.Wasm64) with { Executed = false };
        execution = optimized
            ? execution with { Observations = ImmutableDictionary<int, OracleObservation>.Empty }
            : execution with { OptimizedObservations = ImmutableDictionary<int, OracleObservation>.Empty };
        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(Fixture() with { ExecuteWasm64 = false }, WasmTarget.Wasm64, execution));
    }

    [Fact]
    public void RejectsAnExecutionLabelledWithAnotherTarget() =>
        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(Fixture(), WasmTarget.Wasm64, Execution(WasmTarget.Wasm32)));

    [Theory]
    [InlineData(null, "sha")]
    [InlineData(" ", "sha")]
    [InlineData("module", null)]
    [InlineData("module", " ")]
    public void RejectsMissingOptimizedArtifactIdentity(string? path, string? hash) =>
        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(Fixture(), WasmTarget.Wasm32, Execution(WasmTarget.Wasm32) with
            {
                OptimizedModulePath = path,
                OptimizedModuleSha256 = hash,
            }));

    [Fact]
    public void RejectsMissingDirectInput() =>
        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(Fixture(), WasmTarget.Wasm32, Execution(WasmTarget.Wasm32) with
            {
                Observations = ImmutableDictionary<int, OracleObservation>.Empty,
            }));

    [Fact]
    public void RejectsMissingOptimizedInput() =>
        Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify(Fixture(), WasmTarget.Wasm32, Execution(WasmTarget.Wasm32) with
            {
                OptimizedObservations = ImmutableDictionary<int, OracleObservation>.Empty,
            }));

    [Fact]
    public void DoesNotRequireUnrequestedOptimization() =>
        _verifier.Verify(Fixture() with { ExecuteOptimizedWasm = false }, WasmTarget.Wasm32,
            Execution(WasmTarget.Wasm32) with
            {
                OptimizedModulePath = null,
                OptimizedModuleSha256 = null,
                OptimizedObservations = ImmutableDictionary<int, OracleObservation>.Empty,
            });

    [Fact]
    public void RejectsNullFixture() =>
        Assert.Throws<ArgumentNullException>(() =>
            _verifier.Verify(null!, WasmTarget.Wasm32, Execution(WasmTarget.Wasm32)));

    [Fact]
    public void RejectsNullExecution() =>
        Assert.Throws<ArgumentNullException>(() =>
            _verifier.Verify(Fixture(), WasmTarget.Wasm32, null!));

    private static CorpusFixture Fixture() => new("Execution", "Unused", "", [3])
    {
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
    };

    private static NetWasmExecution Execution(WasmTarget target)
    {
        var observations = ImmutableDictionary<int, OracleObservation>.Empty.Add(
            3, new(OracleObservationKind.Value, 100, null, 0));
        return new(observations, null, "module", "sha", target, true)
        {
            OptimizedObservations = observations,
            OptimizedModulePath = "optimized",
            OptimizedModuleSha256 = "optimized-sha",
        };
    }
}
