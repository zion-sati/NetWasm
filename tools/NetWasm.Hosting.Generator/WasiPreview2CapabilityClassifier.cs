using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;

namespace NetWasm.Hosting.Generator;

internal interface IWasiPreview2CapabilityClassifier
{
    NetWasmPlatformCapability Classify(string module);
}

internal sealed class WasiPreview2CapabilityClassifier : IWasiPreview2CapabilityClassifier
{
    private static readonly ImmutableDictionary<string, NetWasmPlatformCapability> Capabilities =
        CreateCapabilities();

    public NetWasmPlatformCapability Classify(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return Capabilities.TryGetValue(module, out var capability)
            ? capability
            : throw new InvalidDataException(
                $"WASI Preview 2 interface '{module}' has no explicit capability policy.");
    }

    private static ImmutableDictionary<string, NetWasmPlatformCapability> CreateCapabilities()
    {
        var entries = new Dictionary<string, NetWasmPlatformCapability>(StringComparer.Ordinal)
        {
            ["wasi:cli/environment@0.2.11"] = NetWasmPlatformCapability.Environment,
            ["wasi:cli/exit@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/stderr@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/stdin@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/stdout@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-input@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-output@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-stderr@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-stdin@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-stdout@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:clocks/monotonic-clock@0.2.11"] = NetWasmPlatformCapability.MonotonicClock,
            ["wasi:clocks/wall-clock@0.2.11"] = NetWasmPlatformCapability.WallClock,
            ["wasi:filesystem/preopens@0.2.11"] = NetWasmPlatformCapability.PreopenedDirectories,
            ["wasi:filesystem/types@0.2.11"] = NetWasmPlatformCapability.PreopenedDirectories,
            ["wasi:http/incoming-handler@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:http/outgoing-handler@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:http/types@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:io/error@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:io/poll@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:io/streams@0.2.11"] = NetWasmPlatformCapability.Baseline,
            ["wasi:random/insecure-seed@0.2.11"] = NetWasmPlatformCapability.Randomness,
            ["wasi:random/insecure@0.2.11"] = NetWasmPlatformCapability.Randomness,
            ["wasi:random/random@0.2.11"] = NetWasmPlatformCapability.Randomness,
            ["wasi:sockets/instance-network@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:sockets/ip-name-lookup@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:sockets/network@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:sockets/tcp-create-socket@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:sockets/tcp@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:sockets/udp-create-socket@0.2.11"] = NetWasmPlatformCapability.Network,
            ["wasi:sockets/udp@0.2.11"] = NetWasmPlatformCapability.Network,
        };
        return entries.ToImmutableDictionary(StringComparer.Ordinal);
    }
}
