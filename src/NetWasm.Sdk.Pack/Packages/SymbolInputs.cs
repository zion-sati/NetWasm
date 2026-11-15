// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Packages;

public sealed record SymbolInputs(
    bool IncludeSymbols,
    string Format,
    ImmutableArray<SymbolInput> Files)
{
    public static SymbolInputs None { get; } = new(false, "snupkg", ImmutableArray<SymbolInput>.Empty);
}
