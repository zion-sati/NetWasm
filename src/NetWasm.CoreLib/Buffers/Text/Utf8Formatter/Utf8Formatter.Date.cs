// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Scalar adaptation: the current profile exposes invariant DateTime text
// directly, without the culture/ICU DateTimeFormat graph.

namespace System.Buffers.Text;

public static partial class Utf8Formatter
{
    public static bool TryFormat(DateTimeOffset value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat))
    {
        ValidateDateFormat(format);
        return TryWrite(
            format.Symbol is 'O' or 'o' ? value.ToString("O") : value.ToString(),
            destination,
            out bytesWritten);
    }

    public static bool TryFormat(DateTime value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat))
    {
        ValidateDateFormat(format);
        return TryWrite(
            format.Symbol is 'O' or 'o' ? value.ToString("O") : value.ToString(),
            destination,
            out bytesWritten);
    }

    private static void ValidateDateFormat(StandardFormat format)
    {
        if (format.IsDefault)
        {
            return;
        }

        if (format.Symbol is not ('G' or 'R' or 'l' or 'O' or 'o'))
        {
            ThrowHelper.ThrowFormatException_BadFormatSpecifier();
        }
    }
}
