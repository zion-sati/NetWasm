using System;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimePackTargetSelector(IRuntimeOptimizationPolicyResolver policies) : IRuntimePackTargetSelector
{
    private readonly IRuntimeOptimizationPolicyResolver _policies = policies ?? throw new ArgumentNullException(nameof(policies));

    public RuntimePackTarget Select(RuntimePackManifest manifest, string target, string? garbageCollector,
        RuntimeWasmOptimization optimization = RuntimeWasmOptimization.Oz)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var policy = _policies.Resolve(optimization);
        var selected = garbageCollector ?? manifest.DefaultGarbageCollector;
        if (!RuntimeGarbageCollectors.IsSupported(selected))
            throw new InvalidOperationException(RuntimeGarbageCollectors.ValidationMessage);
        if (!manifest.TargetLookup.TryGetValue((target, selected), out var result))
            throw new InvalidOperationException("The requested NetWasm runtime target and collector pair is unavailable.");
        if (!policy.PreferNativeSpeed)
            return result;
        var speed = result.Speed ?? throw new InvalidOperationException("The NetWasm runtime speed assets are unavailable.");
        return result with
        {
            RuntimeFootprintBytes = speed.RuntimeFootprintBytes,
            RuntimeArchive = speed.RuntimeArchive,
            CollectorArchive = speed.CollectorArchive,
            PlatformArchives = speed.PlatformArchives,
        };
    }
}
