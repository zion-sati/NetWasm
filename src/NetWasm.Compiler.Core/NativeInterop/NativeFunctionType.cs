// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core.NativeInterop;

/// <summary>
/// A native function's type as its archive defines it: each parameter's and the result's value kind (I4, I8, F4 or F8,
/// NativeInt for a pointer whose width the target gives; Void when it returns nothing). A Wasm object's type cannot say
/// whether the function is variadic (Variadic is null); an LLVM bitcode module's can, and its parameters are then the
/// fixed ones.
/// </summary>
public sealed record NativeFunctionType(ImmutableArray<CliValueKind> Parameters, CliValueKind Result)
{
    public bool? Variadic { get; init; }

    public bool Equals(NativeFunctionType? other) =>
        other is not null && Result == other.Result && Variadic == other.Variadic && Parameters.SequenceEqual(other.Parameters);

    public override int GetHashCode() =>
        Parameters.Aggregate(HashCode.Combine(Result, Variadic), (hash, kind) => unchecked(hash * 31 + (int)kind));
}
