namespace NetWasm.Hosting.Execution;

/// <summary>A Preview 2 clock explicitly granted to the guest.</summary>
public enum NetWasmClock
{
    Wall,
    Monotonic,
}
