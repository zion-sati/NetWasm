using System.Collections.Immutable;

namespace NetWasm.Hosting.Execution;

/// <summary>Explicit root authority supplied for one execution.</summary>
public sealed record NetWasmCapabilityGrants(
    ImmutableArray<string> Environment,
    ImmutableArray<NetWasmPreopenGrant> Preopens,
    NetWasmNetworkPolicy Network,
    ImmutableArray<NetWasmClock> Clocks,
    bool Randomness);
