// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System;

public interface ISpanParsable<TSelf> : IParsable<TSelf>
    where TSelf : ISpanParsable<TSelf>?
{
    static abstract TSelf Parse(ReadOnlySpan<char> s, IFormatProvider? provider);

    static abstract bool TryParse(
        ReadOnlySpan<char> s,
        IFormatProvider? provider,
        [MaybeNullWhen(false)] out TSelf result);
}
