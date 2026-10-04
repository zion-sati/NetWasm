namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeValidatedNativeBinding(
    string EntryPoint,
    string ProviderPath,
    string ProviderSha256,
    string ArchiveMemberName);
