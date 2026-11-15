// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal abstract record ScalarConstant
{
    internal sealed record Int32(int Value) : ScalarConstant;
    internal sealed record Null : ScalarConstant;
    // A literal from ldstr; its identity follows the runtime's literal interning.
    internal sealed record String(string Value) : ScalarConstant;
    // A ReadOnlySpan<byte> over read-only static data, such as a UTF-8 literal.
    internal sealed record Bytes(ImmutableArray<byte> Value) : ScalarConstant
    {
        public bool Equals(Bytes? other) => other is not null && Value.SequenceEqual(other.Value);
        public override int GetHashCode() => Value.Aggregate(Value.Length, (hash, value) => hash * 31 + value);
    }
}

internal sealed record ConstantCallEvaluation(
    MethodInstanceModel Method,
    ImmutableArray<ScalarConstant?> Arguments);
