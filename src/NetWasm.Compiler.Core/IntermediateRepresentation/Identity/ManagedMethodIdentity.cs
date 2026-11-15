// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

public readonly record struct ManagedMethodIdentity
{
    public ManagedMethodIdentity(string canonicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        CanonicalName = canonicalName;
    }

    public string CanonicalName { get; }

    public override string ToString() => CanonicalName;
}
