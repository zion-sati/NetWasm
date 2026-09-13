// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriSchemeEndReader : IUriSchemeEndReader
{
    public int Read(string value)
    {
        var colon = value.IndexOf(':');
        if (colon <= 0)
        {
            return -1;
        }

        for (var index = 0; index < colon; index++)
        {
            var character = value[index];
            if (index == 0
                ? !IsAsciiLetter(character)
                : !IsAsciiLetter(character) &&
                  !IsAsciiDigit(character) &&
                  character != '+' && character != '-' && character != '.')
            {
                return -1;
            }
        }
        return colon;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiDigit(char value) => "0123456789".Contains(value);
}
