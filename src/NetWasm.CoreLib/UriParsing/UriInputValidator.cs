// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriInputValidator : IUriInputValidator
{
    public void Validate(string? uriString, UriKind uriKind)
    {
        if (uriString is null)
        {
            throw new ArgumentNullException(nameof(uriString));
        }

        if (uriKind is < UriKind.RelativeOrAbsolute or > UriKind.Relative)
        {
            throw new ArgumentException(nameof(uriKind));
        }
    }
}
