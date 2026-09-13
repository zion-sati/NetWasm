// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System;

public interface IParsable<TSelf>
    where TSelf : IParsable<TSelf>?
{
    static abstract TSelf Parse(string s, IFormatProvider? provider);

    static abstract bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        [MaybeNullWhen(false)] out TSelf result);
}
