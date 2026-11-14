// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace System.Net.Http.Json;

internal static class JsonHelpers
{
    internal static MediaTypeHeaderValue GetDefaultMediaType() => new("application/json") { CharSet = "utf-8" };

    internal static void EnsureSupportedEncoding(HttpContent content)
    {
        var charset = content.Headers.ContentType?.CharSet;
        if (charset is null)
        {
            return;
        }

        if (charset.Length >= 2 && charset[0] == '\"' && charset[^1] == '\"')
        {
            charset = charset[1..^1];
        }

        if (!charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "The NetWasm System.Net.Http.Json profile supports UTF-8 JSON content only.");
        }
    }
}
