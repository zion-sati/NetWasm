using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;

namespace NetWasm.Hosting.Generator;

internal interface IWasiCapabilityClassifier
{
    NetWasmPlatformCapability Classify(string module);
}

internal sealed class WasiCapabilityClassifier : IWasiCapabilityClassifier
{
    private static readonly ImmutableDictionary<string, NetWasmPlatformCapability> Capabilities =
        CreateCapabilities();

    public NetWasmPlatformCapability Classify(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return Capabilities.TryGetValue(module, out var capability)
            ? capability
            : throw new InvalidDataException(
                $"WASI interface '{module}' has no explicit capability policy.");
    }

    private static ImmutableDictionary<string, NetWasmPlatformCapability> CreateCapabilities()
    {
        var entries = new Dictionary<string, NetWasmPlatformCapability>(StringComparer.Ordinal)
        {
            ["wasi:cli/environment@0.3.2"] = NetWasmPlatformCapability.Environment,
            ["wasi:cli/exit@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/stderr@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/stdin@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/stdout@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-input@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-output@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-stderr@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-stdin@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/terminal-stdout@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:cli/types@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:clocks/monotonic-clock@0.3.2"] = NetWasmPlatformCapability.MonotonicClock,
            ["wasi:clocks/system-clock@0.3.2"] = NetWasmPlatformCapability.WallClock,
            ["wasi:clocks/types@0.3.2"] = NetWasmPlatformCapability.Baseline,
            ["wasi:filesystem/preopens@0.3.2"] = NetWasmPlatformCapability.PreopenedDirectories,
            ["wasi:filesystem/types@0.3.2"] = NetWasmPlatformCapability.PreopenedDirectories,
            ["wasi:http/client@0.3.2"] = NetWasmPlatformCapability.Network,
            ["wasi:http/types@0.3.2"] = NetWasmPlatformCapability.Network,
            ["wasi:random/insecure-seed@0.3.2"] = NetWasmPlatformCapability.Randomness,
            ["wasi:random/insecure@0.3.2"] = NetWasmPlatformCapability.Randomness,
            ["wasi:random/random@0.3.2"] = NetWasmPlatformCapability.Randomness,
            ["wasi:sockets/ip-name-lookup@0.3.2"] = NetWasmPlatformCapability.Network,
            ["wasi:sockets/types@0.3.2"] = NetWasmPlatformCapability.Network,
        };
        return entries.ToImmutableDictionary(StringComparer.Ordinal);
    }
}
