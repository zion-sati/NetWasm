using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

internal static class ExecutionResultFixture
{
    internal static NetWasmExecutionFailure Failure(
        NetWasmFailurePhase phase = NetWasmFailurePhase.Execution,
        string code = "managed.failure",
        string message = "Managed execution failed.") => new(phase, code, message);

    internal static NetWasmExecutionResult Normal(int exitCode = 0) => new(
        1,
        NetWasmCompletionKind.Normal,
        exitCode,
        null,
        []);

    internal static NetWasmExecutionResult Failed(
        NetWasmCompletionKind kind = NetWasmCompletionKind.HostFailure,
        NetWasmExecutionFailure? primary = null,
        params NetWasmExecutionFailure[] cleanupFailures) => new(
            1,
            kind,
            null,
            primary ?? Failure(),
            [.. cleanupFailures]);
}

internal sealed class ExecutionResultValidationStub(Action<NetWasmExecutionResult> validate) : INetWasmExecutionResultValidator
{
    public void Validate(NetWasmExecutionResult result) => validate(result);
}
