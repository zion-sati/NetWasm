using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;

namespace NetWasm.Hosting.Generator;

internal sealed record Preview2ShimIdentity(string Package, string Version);

internal sealed record HostingPlatformCatalog(
    int Schema,
    string World,
    string WitSha256,
    Preview2ShimIdentity Shim,
    ImmutableArray<NetWasmPlatformImportProvider> Providers);
