// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// Each type's base type, resolved once per compile: a compile's metadata does not change, and relationship questions walk
// the same base chains many times (the receiver flow analysis's cast filters ask one per object and bound).
internal sealed class CachingBaseTypeIdentityResolver(IBaseTypeIdentityResolver inner) : IBaseTypeIdentityResolver
{
    private readonly IBaseTypeIdentityResolver _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ConcurrentDictionary<CliTypeIdentity, CliTypeIdentity?> _bases = new();

    public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _bases.GetOrAdd(type, static (key, inner) => inner.GetBaseTypeIdentity(key), _inner);
    }
}
