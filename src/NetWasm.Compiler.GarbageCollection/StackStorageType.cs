// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public readonly record struct StackStorageType(CliTypeIdentity? Type, bool BackingCovered)
{
    public ImmutableHashSet<CliTypeIdentity> CapturedBackings { get; init; } = [];

    // Every incoming path has a complete backing strategy, stable or captured.
    // This is analysis evidence, not proof that emission published those roots.
    public bool CompleteBackingKnown { get; init; }
}
