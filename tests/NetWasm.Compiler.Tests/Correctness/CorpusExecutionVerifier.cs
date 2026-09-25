using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CorpusExecutionVerifier : ICorpusExecutionVerifier
{
    void ICorpusExecutionVerifier.Verify(
        CorpusFixture fixture, WasmTarget requestedTarget, NetWasmExecution execution)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(execution);
        if (execution.Target != requestedTarget)
        {
            throw new InvalidOperationException($"{fixture.Name}: execution target differs from the requested target.");
        }

        var required = requestedTarget == WasmTarget.Wasm32 || fixture.ExecuteWasm64;
        if (!execution.Executed)
        {
            if (required)
            {
                throw new InvalidOperationException($"{fixture.Name}: required {requestedTarget} execution was skipped.");
            }
            if (!execution.Observations.IsEmpty || !execution.OptimizedObservations.IsEmpty)
            {
                throw new InvalidOperationException($"{fixture.Name}: unexecuted target reported observations.");
            }
            return;
        }

        if (fixture.ExecuteOptimizedWasm &&
            (string.IsNullOrWhiteSpace(execution.OptimizedModulePath) ||
             string.IsNullOrWhiteSpace(execution.OptimizedModuleSha256)))
        {
            throw new InvalidOperationException($"{fixture.Name}: required optimized artifact identity is missing.");
        }

        foreach (var input in fixture.Inputs)
        {
            if (!execution.Observations.ContainsKey(input))
            {
                throw new InvalidOperationException($"{fixture.Name}: {requestedTarget} omitted a required direct observation.");
            }
            if (fixture.ExecuteOptimizedWasm && !execution.OptimizedObservations.ContainsKey(input))
            {
                throw new InvalidOperationException($"{fixture.Name}: {requestedTarget} omitted a required optimized observation.");
            }
        }
    }
}
