namespace NetWasm.Hosting.Build.Environment;

/// <summary>A resolved build environment's task outputs, under the stamp of what resolving it read.</summary>
internal sealed record BuildEnvironmentCacheEntry(
    string Stamp,
    Dictionary<string, string> Outputs,
    Dictionary<string, string> Files);
