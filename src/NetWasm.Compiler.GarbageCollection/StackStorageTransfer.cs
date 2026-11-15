// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// A successful instruction replaces one logical stack slot. Variants absent
// from both collections are inactive in that result and must be cleared.
public sealed record StackStorageTransfer(
    StackStorageLocation Destination,
    ImmutableDictionary<CliTypeIdentity, StackStorageLocation> Copies,
    ImmutableHashSet<CliTypeIdentity> Captures);
