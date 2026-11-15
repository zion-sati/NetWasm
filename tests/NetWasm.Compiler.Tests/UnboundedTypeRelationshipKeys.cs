// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Keys that never narrow a pair, for tests whose relationships ignore the type hierarchy.
internal sealed class UnboundedTypeRelationshipKeys : ITypeRelationshipKeyResolver
{
    public static UnboundedTypeRelationshipKeys Instance { get; } = new();

    public TypeRelationshipKeys Resolve(CliTypeIdentity type) =>
        new(type.CanonicalName, ImmutableHashSet<string>.Empty, Unbounded: true);
}
