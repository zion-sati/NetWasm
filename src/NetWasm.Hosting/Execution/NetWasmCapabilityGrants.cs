using System.Collections.Immutable;

namespace NetWasm.Hosting.Execution;

/// <summary>Explicit root authority supplied for one execution.</summary>
public sealed record NetWasmCapabilityGrants(
    ImmutableArray<string> Environment,
    ImmutableArray<NetWasmPreopenGrant> Preopens,
    NetWasmNetworkPolicy Network,
    ImmutableArray<NetWasmClock> Clocks,
    bool Randomness)
{
    /// <summary>
    /// Whether a local host mounts the directory the program starts in, read-write, at its own
    /// absolute path, as dotnet run and dotnet test do.
    /// </summary>
    public bool WorkingDirectory { get; init; }

    /// <summary>
    /// Whether a local host mounts a private temporary directory, made for the run and removed
    /// after it, at /tmp, as dotnet run and dotnet test do.
    /// </summary>
    public bool TemporaryDirectory { get; init; }
}
