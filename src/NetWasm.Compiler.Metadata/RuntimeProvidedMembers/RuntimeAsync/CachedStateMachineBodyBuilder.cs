// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// Each kickoff's rewritten body, built once per reader: the analyses read a method's body many times, and each build
// decodes the kickoff and its MoveNext and rewrites them, where a method compiled with runtime-async=on decodes once.
// The reader belongs to one compilation snapshot, so a body never outlives the IL it came from.
internal sealed class CachedStateMachineBodyBuilder(IStateMachineBodyBuilder inner) : IStateMachineBodyBuilder
{
    private readonly ConcurrentDictionary<string, Lazy<CilMethodBody>> _bodies = new(StringComparer.Ordinal);

    public CilMethodBody Build(MethodInstanceModel kickoff)
    {
        ArgumentNullException.ThrowIfNull(kickoff);
        return _bodies.GetOrAdd(kickoff.CanonicalName, _ => new(() => inner.Build(kickoff))).Value;
    }
}
