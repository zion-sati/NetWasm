namespace NetWasm.Hosting.Capabilities;

/// <summary>The root authority required by a selected platform import provider.</summary>
public enum NetWasmPlatformCapability
{
    Baseline,
    Environment,
    PreopenedDirectories,
    Network,
    WallClock,
    MonotonicClock,
    Randomness,
}
