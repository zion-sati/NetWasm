// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Symbols;

// A runtime type's names as .NET forms them, keyed by its runtime type ID. A program carries none of them
// (issue 225): they go to the type-symbols sidecar.
public sealed record RuntimeTypeSymbol(
    int TypeId,
    string Name,
    string? Namespace,
    string? FullName,
    string DisplayName);
