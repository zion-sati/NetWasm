// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;

namespace NetWasm.Hosting.Generator;

internal sealed record HostingPlatformCatalog(
    int Schema,
    string World,
    string WitSha256,
    ImmutableArray<NetWasmPlatformImportProvider> Providers);
