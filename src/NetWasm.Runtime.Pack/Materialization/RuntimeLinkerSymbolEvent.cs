// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeLinkerSymbolEvent(
    string InputIdentity,
    RuntimeLinkerSymbolEventKind Kind,
    string EntryPoint);

internal enum RuntimeLinkerSymbolEventKind
{
    Reference,
    LazyDefinition,
    Definition,
}
