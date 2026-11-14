// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// From dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (src/libraries/Common/src/System/Text/ValueStringBuilder.AppendSpanFormattable.cs).

namespace System.Text
{
    internal ref partial struct ValueStringBuilder
    {
        internal void AppendSpanFormattable<T>(T value, string? format = null, IFormatProvider? provider = null) where T : ISpanFormattable
        {
            if (value.TryFormat(_chars.Slice(_pos), out int charsWritten, format, provider))
            {
                _pos += charsWritten;
            }
            else
            {
                Append(value.ToString(format, provider));
            }
        }
    }
}
