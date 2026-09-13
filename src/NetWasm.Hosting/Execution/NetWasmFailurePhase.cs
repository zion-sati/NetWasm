namespace NetWasm.Hosting.Execution;

/// <summary>The execution phase that produced a structured failure.</summary>
public enum NetWasmFailurePhase
{
    Validation,
    Instantiation,
    Execution,
    Observation,
    Cleanup,
    Output,
}
