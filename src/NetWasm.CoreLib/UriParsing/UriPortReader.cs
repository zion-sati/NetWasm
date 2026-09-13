// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriPortReader : IUriPortReader
{
    public int Read(string value)
    {
        if (!IsDigits(value))
        {
            throw new UriFormatException("Invalid port.");
        }

        var port = int.Parse(value);
        if (port is < 0 or > 65535)
        {
            throw new UriFormatException("Invalid port.");
        }
        return port;
    }

    private static bool IsDigits(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }
        return true;
    }
}
