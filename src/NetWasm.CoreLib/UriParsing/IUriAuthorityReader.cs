// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal interface IUriAuthorityReader
{
    UriAuthorityParts Read(string authority);
}
