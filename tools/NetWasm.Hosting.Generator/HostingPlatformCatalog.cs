using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;

namespace NetWasm.Hosting.Generator;

internal sealed record HostingPlatformCatalog(
    int Schema,
    string World,
    string WitSha256,
    ImmutableArray<NetWasmPlatformImportProvider> Providers);
