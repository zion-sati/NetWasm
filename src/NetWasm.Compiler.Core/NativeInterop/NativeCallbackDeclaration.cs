// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeCallbackDeclaration(
    ImmutableArray<string> CallingConventions,
    string? EntryPoint,
    bool IsVarArg,
    bool HasUnsupportedNamedArguments)
{
    public bool HasEquivalentFacts(NativeCallbackDeclaration? other) =>
        other is not null &&
        StringComparer.Ordinal.Equals(EntryPoint, other.EntryPoint) &&
        IsVarArg == other.IsVarArg &&
        HasUnsupportedNamedArguments == other.HasUnsupportedNamedArguments &&
        CallingConventions.AsSpan().SequenceEqual(other.CallingConventions.AsSpan());
}
