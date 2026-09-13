namespace NetWasm.Hosting.Execution;

/// <summary>A transport-safe execution failure without an exception object or stack.</summary>
public sealed record NetWasmExecutionFailure(NetWasmFailurePhase Phase, string Code, string Message);
