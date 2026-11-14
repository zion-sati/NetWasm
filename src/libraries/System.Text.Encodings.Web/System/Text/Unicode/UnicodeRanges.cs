// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Text.Unicode;

/// <summary>Contains the predefined ranges needed by the built-in JavaScript encoders.</summary>
public static class UnicodeRanges
{
    /// <summary>An empty range.</summary>
    public static UnicodeRange None { get; } = new UnicodeRange(0, 0);

    /// <summary>The Basic Latin block (U+0000..U+007F).</summary>
    public static UnicodeRange BasicLatin { get; } = UnicodeRange.Create('\u0000', '\u007F');

    /// <summary>The BMP range used by the runtime encoder's relaxed built-in.</summary>
    public static UnicodeRange All { get; } = UnicodeRange.Create('\u0000', '\uFFFF');
}
