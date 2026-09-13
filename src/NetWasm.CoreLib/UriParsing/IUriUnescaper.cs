// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.

namespace System.UriParsing;

internal interface IUriUnescaper
{
    string Unescape(string value);
}
