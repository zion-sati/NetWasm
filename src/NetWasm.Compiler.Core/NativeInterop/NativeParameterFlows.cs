// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core.NativeInterop;

// A P/Invoke parameter's [In] and [Out] attributes (ECMA-335 II.23.1.13), which direct how desktop .NET's runtime
// marshalling copies a by-reference handle: in, out or both. A parameter with neither takes its default.
[Flags]
public enum NativeParameterFlow
{
    None = 0,
    In = 1,
    Out = 2,
}

// The flow of each parameter in declaration order, compared by value so equal declarations read twice stay equal.
public sealed record NativeParameterFlows(ImmutableArray<NativeParameterFlow> Values)
{
    public static NativeParameterFlows None { get; } = new(ImmutableArray<NativeParameterFlow>.Empty);

    public NativeParameterFlow this[int index] =>
        !Values.IsDefault && index < Values.Length ? Values[index] : NativeParameterFlow.None;

    public bool Equals(NativeParameterFlows? other) =>
        other is not null && Values.AsSpan().SequenceEqual(other.Values.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Values.AsSpan())
            hash.Add(value);
        return hash.ToHashCode();
    }
}
