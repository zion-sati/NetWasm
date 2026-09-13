// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Scalar adaptation: invariant TimeSpan.ToString supplies the profile's
// culture-independent constant representation.

namespace System.Buffers.Text;

public static partial class Utf8Formatter
{
    public static bool TryFormat(TimeSpan value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat))
    {
        if (!format.IsDefault && format.Symbol is not ('c' or 't' or 'T' or 'G' or 'g'))
        {
            ThrowHelper.ThrowFormatException_BadFormatSpecifier();
        }

        return TryWrite(value.ToString(), destination, out bytesWritten);
    }
}
