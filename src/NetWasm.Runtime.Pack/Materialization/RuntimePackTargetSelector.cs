using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimePackTargetSelector : IRuntimePackTargetSelector
{
    public RuntimePackTarget Select(RuntimePackManifest manifest, string target, string? garbageCollector)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var selected = garbageCollector ?? manifest.DefaultGarbageCollector;
        if (selected is not ("Compact" or "Boehm"))
            throw new InvalidOperationException("The NetWasm garbage collector must be Compact or Boehm.");
        return manifest.TargetLookup.TryGetValue((target, selected), out var result)
            ? result
            : throw new InvalidOperationException("The requested NetWasm runtime target and collector pair is unavailable.");
    }
}
